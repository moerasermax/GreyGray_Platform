using System.Text;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Catalog.Core;

internal sealed class CatalogService(
    ICatalogRepository repository,
    IEventPublisher eventPublisher,
    IClock clock,
    ICorrelationContext correlationContext)
    : ICatalogQuery, IStorefrontCatalogQuery, ICatalogAdministration
{
    public async Task<Result<SkuSnapshot>> GetSkuAsync(
        SkuId id,
        CancellationToken cancellationToken)
    {
        var sku = await repository.FindSkuAsync(id, correlationContext.TenantId, cancellationToken);
        return sku is null
            ? Result<SkuSnapshot>.Failure("catalog.sku-not-found", "找不到 SKU。")
            : sku.ToSnapshot();
    }

    public async Task<Result<IReadOnlyList<SkuSnapshot>>> GetSkusAsync(
        IReadOnlyCollection<SkuId> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var snapshots = new List<SkuSnapshot>(ids.Count);
        foreach (var id in ids.Distinct())
        {
            var sku = await repository.FindSkuAsync(id, correlationContext.TenantId, cancellationToken);
            if (sku is null)
            {
                return Result<IReadOnlyList<SkuSnapshot>>.Failure(
                    "catalog.sku-not-found", $"找不到 SKU {id}。");
            }

            snapshots.Add(sku.ToSnapshot());
        }

        return snapshots;
    }

    async Task<IReadOnlyList<CategoryView>> IStorefrontCatalogQuery.ListCategoriesAsync(
        CancellationToken cancellationToken) =>
        (await repository.ListPublishedCategoriesAsync(correlationContext.TenantId, cancellationToken))
        .Select(value => value.ToView())
        .ToArray();

    public async Task<IReadOnlyList<CategoryView>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        (await repository.ListCategoriesAsync(correlationContext.TenantId, cancellationToken))
        .Select(value => value.ToView())
        .ToArray();

    async Task<Result<CursorPage<StorefrontProductListItem>>> IStorefrontCatalogQuery.ListProductsAsync(
        ProductSearch search,
        CancellationToken cancellationToken)
    {
        var page = ValidateSearch(search);
        if (page.IsFailure)
        {
            return Result<CursorPage<StorefrontProductListItem>>.Failure(page.Error);
        }

        var products = await FilterProductsAsync(search with { IncludeArchived = false }, cancellationToken);
        var visible = new List<StorefrontProductListItem>();
        foreach (var product in products)
        {
            var activeSkus = (await repository.ListSkusAsync(product.Id, cancellationToken))
                .Where(value => value.IsActive)
                .ToArray();
            var listedSkus = product.Mode == FulfillmentMode.Stock
                ? activeSkus.Where(value => value.ListPrice is not null).ToArray()
                : activeSkus;
            if (listedSkus.Length == 0)
            {
                continue;
            }

            var images = await repository.ListImagesAsync(product.Id, cancellationToken);
            var representative = listedSkus
                .OrderBy(value => value.ListPrice?.AmountMinor ?? long.MaxValue)
                .First();
            visible.Add(new StorefrontProductListItem(
                product.Id,
                product.Name,
                product.ShortDescription,
                images.FirstOrDefault()?.Url,
                representative.ListPrice,
                BuildUnitPriceLabel(representative),
                product.Mode));
        }

        return Slice(visible, search);
    }

    public async Task<Result<StorefrontProductDetail>> GetProductAsync(
        ProductId productId,
        CancellationToken cancellationToken)
    {
        var product = await repository.FindProductAsync(
            productId,
            correlationContext.TenantId,
            cancellationToken);
        if (product is null || !product.IsActive)
        {
            return Result<StorefrontProductDetail>.Failure("catalog.product-not-found", "找不到商品。");
        }

        var skus = (await repository.ListSkusAsync(productId, cancellationToken))
            .Where(value => value.IsActive)
            .Select(value => value.ToView())
            .ToArray();
        if (skus.Length == 0)
        {
            return Result<StorefrontProductDetail>.Failure("catalog.product-not-found", "找不到商品。");
        }

        var images = await repository.ListImagesAsync(productId, cancellationToken);
        return new StorefrontProductDetail(
            product.Id,
            product.Name,
            product.Description,
            product.ShortDescription,
            product.CategoryId,
            images.Select(value => value.Url).ToArray(),
            product.Mode,
            skus);
    }

    public async Task<Result<CategoryView>> CreateCategoryAsync(
        CategoryInput input,
        CancellationToken cancellationToken)
    {
        var validated = ValidateCategory(input);
        if (validated.IsFailure)
        {
            return Result<CategoryView>.Failure(validated.Error);
        }

        var category = Category.Create(
            CategoryId.New(),
            correlationContext.TenantId,
            validated.Value.Name,
            validated.Value.ImageUrl,
            validated.Value.SortOrder);
        repository.AddCategory(category);
        await repository.SaveChangesAsync(cancellationToken);
        return category.ToView();
    }

    public async Task<Result<CategoryView>> UpdateCategoryAsync(
        CategoryId categoryId,
        CategoryInput input,
        CancellationToken cancellationToken)
    {
        var category = await repository.FindCategoryAsync(
            categoryId,
            correlationContext.TenantId,
            cancellationToken);
        if (category is null)
        {
            return Result<CategoryView>.Failure("catalog.category-not-found", "找不到分類。");
        }

        var validated = ValidateCategory(input);
        if (validated.IsFailure)
        {
            return Result<CategoryView>.Failure(validated.Error);
        }

        category.Update(validated.Value.Name, validated.Value.ImageUrl, validated.Value.SortOrder);
        await repository.SaveChangesAsync(cancellationToken);
        return category.ToView();
    }

    public async Task<Result<CursorPage<AdminProductView>>> ListProductsAsync(
        ProductSearch search,
        CancellationToken cancellationToken)
    {
        var validation = ValidateSearch(search);
        if (validation.IsFailure)
        {
            return Result<CursorPage<AdminProductView>>.Failure(validation.Error);
        }

        var products = await FilterProductsAsync(search, cancellationToken);
        var views = new List<AdminProductView>(products.Count);
        foreach (var product in products)
        {
            views.Add(await ToAdminViewAsync(product, cancellationToken));
        }

        return Slice(views, search);
    }

    async Task<Result<AdminProductView>> ICatalogAdministration.GetProductAsync(
        ProductId productId,
        CancellationToken cancellationToken)
    {
        var product = await repository.FindProductAsync(
            productId,
            correlationContext.TenantId,
            cancellationToken);
        return product is null
            ? Result<AdminProductView>.Failure("catalog.product-not-found", "找不到商品。")
            : await ToAdminViewAsync(product, cancellationToken);
    }

    public async Task<Result<AdminProductView>> CreateProductAsync(
        AdminProductInput input,
        CancellationToken cancellationToken)
    {
        var validated = await ValidateProductAsync(input, cancellationToken);
        if (validated.IsFailure)
        {
            return Result<AdminProductView>.Failure(validated.Error);
        }

        var product = Product.Create(
            ProductId.New(),
            correlationContext.TenantId,
            validated.Value.Data,
            clock.UtcNow);
        repository.AddProduct(product);
        await repository.ReplaceImagesAsync(product.Id, validated.Value.Images, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return ToAdminView(product, validated.Value.Images, []);
    }

    public async Task<Result<AdminProductView>> UpdateProductAsync(
        ProductId productId,
        AdminProductInput input,
        CancellationToken cancellationToken)
    {
        var product = await repository.FindProductAsync(
            productId,
            correlationContext.TenantId,
            cancellationToken);
        if (product is null)
        {
            return Result<AdminProductView>.Failure("catalog.product-not-found", "找不到商品。");
        }

        var validated = await ValidateProductAsync(input, cancellationToken);
        if (validated.IsFailure)
        {
            return Result<AdminProductView>.Failure(validated.Error);
        }

        product.Update(validated.Value.Data);
        await repository.ReplaceImagesAsync(product.Id, validated.Value.Images, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        var skus = await repository.ListSkusAsync(product.Id, cancellationToken);
        return ToAdminView(product, validated.Value.Images, skus);
    }

    public async Task<Result<AdminSkuView>> CreateSkuAsync(
        ProductId productId,
        AdminSkuInput input,
        CancellationToken cancellationToken)
    {
        var product = await repository.FindProductAsync(
            productId,
            correlationContext.TenantId,
            cancellationToken);
        if (product is null)
        {
            return Result<AdminSkuView>.Failure("catalog.product-not-found", "找不到商品。");
        }

        var data = ValidateSku(input);
        if (data.IsFailure)
        {
            return Result<AdminSkuView>.Failure(data.Error);
        }

        var sku = Sku.Create(
            SkuId.New(),
            productId,
            correlationContext.TenantId,
            data.Value,
            clock.UtcNow);
        repository.AddSku(sku);
        if (sku.IsActive)
        {
            await eventPublisher.PublishAsync(
                new SkuPublished(
                    Guid.CreateVersion7(),
                    clock.UtcNow,
                    correlationContext.TenantId,
                    sku.Id,
                    productId,
                    sku.Name),
                cancellationToken);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return sku.ToView();
    }

    public async Task<Result<AdminSkuView>> UpdateSkuAsync(
        SkuId skuId,
        AdminSkuInput input,
        CancellationToken cancellationToken)
    {
        var sku = await repository.FindSkuAsync(skuId, correlationContext.TenantId, cancellationToken);
        if (sku is null)
        {
            return Result<AdminSkuView>.Failure("catalog.sku-not-found", "找不到 SKU。");
        }

        var data = ValidateSku(input);
        if (data.IsFailure)
        {
            return Result<AdminSkuView>.Failure(data.Error);
        }

        var wasActive = sku.IsActive;
        var attributesChanged = sku.Name != data.Value.Name ||
                                sku.VariantName != data.Value.VariantName ||
                                sku.WeightGram != data.Value.WeightGram ||
                                sku.Size != data.Value.Size;
        sku.Update(data.Value);
        var now = clock.UtcNow;

        if (!wasActive && sku.IsActive)
        {
            await eventPublisher.PublishAsync(
                new SkuPublished(
                    Guid.CreateVersion7(), now, correlationContext.TenantId,
                    sku.Id, sku.ProductId, sku.Name),
                cancellationToken);
        }
        else if (wasActive && !sku.IsActive)
        {
            await eventPublisher.PublishAsync(
                new SkuArchived(
                    Guid.CreateVersion7(), now, correlationContext.TenantId, sku.Id),
                cancellationToken);
        }

        if (attributesChanged)
        {
            await eventPublisher.PublishAsync(
                new SkuAttributesChanged(
                    Guid.CreateVersion7(), now, correlationContext.TenantId,
                    sku.Id, sku.WeightGram, sku.Size),
                cancellationToken);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return sku.ToView();
    }

    private async Task<IReadOnlyList<Product>> FilterProductsAsync(
        ProductSearch search,
        CancellationToken cancellationToken)
    {
        IEnumerable<Product> query = await repository.ListProductsAsync(
            correlationContext.TenantId,
            cancellationToken);
        if (!search.IncludeArchived)
        {
            query = query.Where(value => value.IsActive);
        }

        if (search.CategoryId is { } categoryId)
        {
            query = query.Where(value => value.CategoryId == categoryId);
        }

        if (search.Mode is { } mode)
        {
            query = query.Where(value => value.Mode == mode);
        }

        if (!string.IsNullOrWhiteSpace(search.Query))
        {
            query = query.Where(value =>
                value.Name.Contains(search.Query.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var after = DecodeCursor(search.Cursor);
        return query
            .Where(value => after is null || value.Id.Value.CompareTo(after.Value) > 0)
            .OrderBy(value => value.Id.Value)
            .ToArray();
    }

    private async Task<AdminProductView> ToAdminViewAsync(
        Product product,
        CancellationToken cancellationToken) =>
        ToAdminView(
            product,
            (await repository.ListImagesAsync(product.Id, cancellationToken)).Select(value => value.Url).ToArray(),
            await repository.ListSkusAsync(product.Id, cancellationToken));

    private static AdminProductView ToAdminView(
        Product product,
        IReadOnlyList<string> images,
        IReadOnlyList<Sku> skus) =>
        new(
            product.Id,
            product.Name,
            product.Description,
            product.ShortDescription,
            product.CategoryId,
            product.Mode,
            images,
            product.IsActive,
            skus.Select(value => value.ToView()).ToArray());

    private async Task<Result<ValidatedProduct>> ValidateProductAsync(
        AdminProductInput input,
        CancellationToken cancellationToken)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100 || input.ShortDescription?.Trim().Length > 100)
        {
            return Result<ValidatedProduct>.Failure("catalog.invalid-product", "商品名稱或短描述格式不正確。");
        }

        if (input.CategoryId is { } categoryId &&
            await repository.FindCategoryAsync(categoryId, correlationContext.TenantId, cancellationToken) is null)
        {
            return Result<ValidatedProduct>.Failure("catalog.category-not-found", "找不到分類。");
        }

        var images = (input.Images ?? []).Select(value => value.Trim()).ToArray();
        if (images.Any(value => string.IsNullOrEmpty(value) || value.Length > 2048 ||
                                !Uri.TryCreate(value, UriKind.Absolute, out _)))
        {
            return Result<ValidatedProduct>.Failure("catalog.invalid-image-url", "商品圖片必須是有效的絕對 URL。");
        }

        return new ValidatedProduct(
            new ProductData(
                name,
                NormalizeOptional(input.Description),
                NormalizeOptional(input.ShortDescription),
                input.CategoryId,
                input.Mode,
                input.IsActive),
            images);
    }

    private static Result<CategoryInput> ValidateCategory(CategoryInput input)
    {
        var name = input.Name?.Trim();
        var imageUrl = NormalizeOptional(input.ImageUrl);
        if (string.IsNullOrEmpty(name) || name.Length > 50 ||
            imageUrl is not null && (!Uri.TryCreate(imageUrl, UriKind.Absolute, out _) || imageUrl.Length > 2048))
        {
            return Result<CategoryInput>.Failure("catalog.invalid-category", "分類名稱或圖片網址格式不正確。");
        }

        return new CategoryInput(name, imageUrl, input.SortOrder);
    }

    private static Result<SkuData> ValidateSku(AdminSkuInput input)
    {
        var name = input.Name?.Trim();
        var variantName = NormalizeOptional(input.VariantName);
        var unit = NormalizeOptional(input.UnitOfMeasure);
        if (string.IsNullOrEmpty(name) || name.Length > 100 || variantName?.Length > 50 ||
            input.WeightGram < 0 || input.Size.LengthCm < 0 || input.Size.WidthCm < 0 ||
            input.Size.HeightCm < 0 || input.UnitCount is <= 0 || input.ListPrice?.IsNegative == true)
        {
            return Result<SkuData>.Failure(
                "catalog.invalid-sku",
                "SKU 欄位格式不正確；重量與三邊尺寸在 M1a 起皆為必填且不得為負數。");
        }

        return new SkuData(
            name,
            variantName,
            input.WeightGram,
            input.Size,
            unit,
            input.UnitCount,
            input.ListPrice,
            input.IsActive);
    }

    private static Result ValidateSearch(ProductSearch search) =>
        search.Limit is >= 1 and <= 100 && DecodeCursor(search.Cursor, false) is not InvalidCursor
            ? Result.Success()
            : Result.Failure("catalog.invalid-cursor", "cursor 無效或 limit 不在 1 到 100 之間。");

    private static Result<CursorPage<T>> Slice<T>(IReadOnlyList<T> values, ProductSearch search)
        where T : class
    {
        var selected = values.Take(search.Limit + 1).ToArray();
        var hasMore = selected.Length > search.Limit;
        var items = selected.Take(search.Limit).ToArray();
        if (!hasMore || items.Length == 0)
        {
            return new CursorPage<T>(items, null);
        }

        var id = items[^1] switch
        {
            StorefrontProductListItem item => item.Id.Value,
            AdminProductView item => item.Id.Value,
            _ => throw new InvalidOperationException("不支援的 Catalog cursor item。"),
        };
        return new CursorPage<T>(items, Convert.ToBase64String(id.ToByteArray()));
    }

    private static Guid? DecodeCursor(string? cursor) =>
        DecodeCursor(cursor, true) is Guid value ? value : null;

    private static object? DecodeCursor(string? cursor, bool throwOnInvalid)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(cursor);
            return bytes.Length == 16 ? new Guid(bytes) : InvalidCursor.Instance;
        }
        catch (FormatException) when (!throwOnInvalid)
        {
            return InvalidCursor.Instance;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string? BuildUnitPriceLabel(Sku sku) =>
        sku.UnitCount is { } count && sku.UnitOfMeasure is { } unit
            ? $"{count} {unit}"
            : null;

    private sealed record ValidatedProduct(ProductData Data, IReadOnlyList<string> Images);
    private sealed class InvalidCursor
    {
        public static readonly InvalidCursor Instance = new();
        private InvalidCursor()
        {
        }
    }
}
