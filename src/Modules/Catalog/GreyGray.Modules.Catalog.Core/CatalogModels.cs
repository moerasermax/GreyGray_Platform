using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Catalog.Core;

internal sealed class Category
{
    private Category()
    {
    }

    private Category(CategoryId id, TenantId tenantId, string name, string? imageUrl, int sortOrder)
    {
        Id = id;
        TenantId = tenantId;
        Update(name, imageUrl, sortOrder);
    }

    public CategoryId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? ImageUrl { get; private set; }
    public int SortOrder { get; private set; }

    public static Category Create(
        CategoryId id,
        TenantId tenantId,
        string name,
        string? imageUrl,
        int sortOrder) => new(id, tenantId, name, imageUrl, sortOrder);

    public void Update(string name, string? imageUrl, int sortOrder)
    {
        Name = name;
        ImageUrl = imageUrl;
        SortOrder = sortOrder;
    }

    public CategoryView ToView() => new(Id, Name, ImageUrl, SortOrder);
}

internal sealed class Product
{
    private Product()
    {
    }

    private Product(
        ProductId id,
        TenantId tenantId,
        ProductData data,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        Update(data);
        CreatedAt = createdAt;
    }

    public ProductId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? ShortDescription { get; private set; }
    public CategoryId? CategoryId { get; private set; }
    public FulfillmentMode Mode { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Product Create(
        ProductId id,
        TenantId tenantId,
        ProductData data,
        DateTimeOffset createdAt) => new(id, tenantId, data, createdAt);

    public void Update(ProductData data)
    {
        Name = data.Name;
        Description = data.Description;
        ShortDescription = data.ShortDescription;
        CategoryId = data.CategoryId;
        Mode = data.Mode;
        IsActive = data.IsActive;
    }
}

internal sealed record ProductData(
    string Name,
    string? Description,
    string? ShortDescription,
    CategoryId? CategoryId,
    FulfillmentMode Mode,
    bool IsActive);

internal sealed class ProductImage
{
    private ProductImage()
    {
    }

    private ProductImage(ProductId productId, int position, string url)
    {
        ProductId = productId;
        Position = position;
        Url = url;
    }

    public ProductId ProductId { get; private set; }
    public int Position { get; private set; }
    public string Url { get; private set; } = string.Empty;

    public static ProductImage Create(ProductId productId, int position, string url) =>
        new(productId, position, url);
}

internal sealed class Sku
{
    private Sku()
    {
    }

    private Sku(
        SkuId id,
        ProductId productId,
        TenantId tenantId,
        SkuData data,
        DateTimeOffset createdAt)
    {
        Id = id;
        ProductId = productId;
        TenantId = tenantId;
        Update(data);
        CreatedAt = createdAt;
    }

    public SkuId Id { get; private set; }
    public ProductId ProductId { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? VariantName { get; private set; }
    public int WeightGram { get; private set; }
    public int LengthCm { get; private set; }
    public int WidthCm { get; private set; }
    public int HeightCm { get; private set; }
    public string? UnitOfMeasure { get; private set; }
    public int? UnitCount { get; private set; }
    public long? ListPriceAmountMinor { get; private set; }
    public Currency? ListPriceCurrency { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Dimensions Size => new(LengthCm, WidthCm, HeightCm);
    public Money? ListPrice => ListPriceAmountMinor is { } amount && ListPriceCurrency is { } currency
        ? new Money(amount, currency)
        : null;

    public static Sku Create(
        SkuId id,
        ProductId productId,
        TenantId tenantId,
        SkuData data,
        DateTimeOffset createdAt) => new(id, productId, tenantId, data, createdAt);

    public void Update(SkuData data)
    {
        Name = data.Name;
        VariantName = data.VariantName;
        WeightGram = data.WeightGram;
        LengthCm = data.Size.LengthCm;
        WidthCm = data.Size.WidthCm;
        HeightCm = data.Size.HeightCm;
        UnitOfMeasure = data.UnitOfMeasure;
        UnitCount = data.UnitCount;
        ListPriceAmountMinor = data.ListPrice?.AmountMinor;
        ListPriceCurrency = data.ListPrice?.Currency;
        IsActive = data.IsActive;
    }

    public SkuSnapshot ToSnapshot() => new(
        Id,
        ProductId,
        Name,
        VariantName,
        WeightGram,
        Size,
        IsActive)
    {
        UnitOfMeasure = UnitOfMeasure,
        UnitCount = UnitCount,
        ListPrice = ListPrice,
    };

    public AdminSkuView ToView() => new(
        Id,
        ProductId,
        Name,
        VariantName,
        WeightGram,
        Size,
        UnitOfMeasure,
        UnitCount,
        ListPrice,
        IsActive);
}

internal sealed record SkuData(
    string Name,
    string? VariantName,
    int WeightGram,
    Dimensions Size,
    string? UnitOfMeasure,
    int? UnitCount,
    Money? ListPrice,
    bool IsActive);

internal sealed class Favorite
{
    private Favorite()
    {
    }

    private Favorite(TenantId tenantId, CustomerId customerId, ProductId productId, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        CustomerId = customerId;
        ProductId = productId;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public ProductId ProductId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Favorite Create(
        TenantId tenantId,
        CustomerId customerId,
        ProductId productId,
        DateTimeOffset createdAt) => new(tenantId, customerId, productId, createdAt);
}

internal sealed record FavoriteCursor(DateTimeOffset CreatedAt, ProductId ProductId);

internal sealed record FavoriteProduct(Product Product, DateTimeOffset CreatedAt);

internal interface ICatalogRepository
{
    Task<Category?> FindCategoryAsync(CategoryId id, TenantId tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Category>> ListCategoriesAsync(TenantId tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Category>> ListPublishedCategoriesAsync(TenantId tenantId, CancellationToken cancellationToken);
    Task<Product?> FindProductAsync(ProductId id, TenantId tenantId, CancellationToken cancellationToken);
    Task<Sku?> FindSkuAsync(SkuId id, TenantId tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Product>> ListProductsAsync(TenantId tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Sku>> ListSkusAsync(ProductId productId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductImage>> ListImagesAsync(ProductId productId, CancellationToken cancellationToken);
    Task<bool> IsStorefrontVisibleAsync(ProductId productId, TenantId tenantId, CancellationToken cancellationToken);
    Task InsertFavoriteAsync(Favorite favorite, CancellationToken cancellationToken);
    Task DeleteFavoriteAsync(
        TenantId tenantId,
        CustomerId customerId,
        ProductId productId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<FavoriteProduct>> ListFavoritesAsync(
        TenantId tenantId,
        CustomerId customerId,
        FavoriteCursor? cursor,
        int take,
        CancellationToken cancellationToken);
    Task<IReadOnlySet<ProductId>> FindFavoriteProductIdsAsync(
        TenantId tenantId,
        CustomerId customerId,
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken);
    void AddCategory(Category category);
    void AddProduct(Product product);
    void AddSku(Sku sku);
    Task ReplaceImagesAsync(
        ProductId productId,
        IReadOnlyList<string> images,
        CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
