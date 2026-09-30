using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Catalog.Core;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GreyGray.Modules.Catalog.Infra;

internal sealed class CatalogRepository(CatalogDbContext dbContext) : ICatalogRepository
{
    public Task<Category?> FindCategoryAsync(
        CategoryId id,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        dbContext.Categories.SingleOrDefaultAsync(
            value => value.Id == id && value.TenantId == tenantId,
            cancellationToken);

    public Task<bool> HasChildCategoriesAsync(
        CategoryId id,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        dbContext.Categories.AnyAsync(
            value => value.TenantId == tenantId && value.ParentId == id,
            cancellationToken);

    public async Task<IReadOnlyList<Category>> ListCategoriesAsync(
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        await dbContext.Categories
            .Where(value => value.TenantId == tenantId)
            .OrderBy(value => value.SortOrder)
            .ThenBy(value => value.Name)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Category>> ListPublishedCategoriesAsync(
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        await dbContext.Categories
            .Where(category => category.TenantId == tenantId &&
                dbContext.Products.Any(product =>
                    product.TenantId == tenantId &&
                    (product.CategoryId == category.Id ||
                     dbContext.Categories.Any(child =>
                         child.TenantId == tenantId &&
                         child.ParentId == category.Id &&
                         product.CategoryId == child.Id)) &&
                    product.IsActive &&
                    dbContext.Skus.Any(sku =>
                        sku.ProductId == product.Id &&
                        sku.IsActive &&
                        (product.Mode == FulfillmentMode.Preorder ||
                         sku.ListPriceAmountMinor != null &&
                         sku.ListPriceCurrency != null))))
            .OrderBy(value => value.SortOrder)
            .ThenBy(value => value.Name)
            .ToArrayAsync(cancellationToken);

    public Task<Product?> FindProductAsync(
        ProductId id,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        dbContext.Products.SingleOrDefaultAsync(
            value => value.Id == id && value.TenantId == tenantId,
            cancellationToken);

    public Task<Sku?> FindSkuAsync(
        SkuId id,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        dbContext.Skus.SingleOrDefaultAsync(
            value => value.Id == id && value.TenantId == tenantId,
            cancellationToken);

    public async Task<IReadOnlyList<Product>> ListProductsAsync(
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        await dbContext.Products
            .Where(value => value.TenantId == tenantId)
            .OrderBy(value => value.Id)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Sku>> ListSkusAsync(
        ProductId productId,
        CancellationToken cancellationToken) =>
        await dbContext.Skus
            .Where(value => value.ProductId == productId)
            .OrderBy(value => value.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductImage>> ListImagesAsync(
        ProductId productId,
        CancellationToken cancellationToken) =>
        await dbContext.ProductImages
            .Where(value => value.ProductId == productId)
            .OrderBy(value => value.Position)
            .ToArrayAsync(cancellationToken);

    public Task<bool> IsStorefrontVisibleAsync(
        ProductId productId,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        dbContext.Products.AnyAsync(
            product => product.Id == productId &&
                product.TenantId == tenantId &&
                product.IsActive &&
                dbContext.Skus.Any(sku =>
                    sku.TenantId == tenantId &&
                    sku.ProductId == product.Id &&
                    sku.IsActive),
            cancellationToken);

    public async Task InsertFavoriteAsync(Favorite favorite, CancellationToken cancellationToken) =>
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO catalog.favorite (tenant_id, customer_id, product_id, created_at)
            VALUES ({favorite.TenantId.Value}, {favorite.CustomerId.Value},
                    {favorite.ProductId.Value}, {favorite.CreatedAt})
            ON CONFLICT DO NOTHING;
            """, cancellationToken);

    public async Task DeleteFavoriteAsync(
        TenantId tenantId,
        CustomerId customerId,
        ProductId productId,
        CancellationToken cancellationToken) =>
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM catalog.favorite
            WHERE tenant_id = {tenantId.Value}
              AND customer_id = {customerId.Value}
              AND product_id = {productId.Value};
            """, cancellationToken);

    public async Task<IReadOnlyList<FavoriteProduct>> ListFavoritesAsync(
        TenantId tenantId,
        CustomerId customerId,
        FavoriteCursor? cursor,
        int take,
        CancellationToken cancellationToken)
    {
        var favorites = dbContext.Favorites
            .AsNoTracking()
            .Where(favorite =>
                favorite.TenantId == tenantId &&
                favorite.CustomerId == customerId &&
                dbContext.Products.Any(product =>
                    product.Id == favorite.ProductId &&
                    product.TenantId == tenantId &&
                    product.IsActive &&
                    dbContext.Skus.Any(sku =>
                        sku.TenantId == tenantId &&
                        sku.ProductId == product.Id &&
                        sku.IsActive)));

        if (cursor is not null)
        {
            favorites = favorites.Where(favorite =>
                favorite.CreatedAt < cursor.CreatedAt ||
                favorite.CreatedAt == cursor.CreatedAt && favorite.ProductId < cursor.ProductId);
        }

        return await (
            from favorite in favorites
            join product in dbContext.Products.AsNoTracking()
                on new { favorite.TenantId, favorite.ProductId }
                equals new { product.TenantId, ProductId = product.Id }
            orderby favorite.CreatedAt descending, favorite.ProductId descending
            select new FavoriteProduct(product, favorite.CreatedAt))
            .Take(take)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<ProductId>> FindFavoriteProductIdsAsync(
        TenantId tenantId,
        CustomerId customerId,
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken) =>
        (await dbContext.Favorites
            .AsNoTracking()
            .Where(favorite =>
                favorite.TenantId == tenantId &&
                favorite.CustomerId == customerId &&
                productIds.Contains(favorite.ProductId))
            .Select(favorite => favorite.ProductId)
            .ToArrayAsync(cancellationToken))
        .ToHashSet();

    public void AddCategory(Category category) => dbContext.Categories.Add(category);
    public void AddProduct(Product product) => dbContext.Products.Add(product);
    public void AddSku(Sku sku) => dbContext.Skus.Add(sku);

    public async Task ReplaceImagesAsync(
        ProductId productId,
        IReadOnlyList<string> images,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.ProductImages
            .Where(value => value.ProductId == productId)
            .ToArrayAsync(cancellationToken);
        dbContext.ProductImages.RemoveRange(existing);
        for (var index = 0; index < images.Count; index++)
        {
            dbContext.ProductImages.Add(ProductImage.Create(productId, index, images[index]));
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgres &&
                  MapCategoryHierarchyError(postgres) is not null)
        {
            throw new CatalogPersistenceConflictException(
                MapCategoryHierarchyError(postgres)!,
                exception);
        }
    }

    internal static string? MapCategoryHierarchyError(PostgresException exception) =>
        (exception.SqlState, exception.ConstraintName) switch
        {
            (PostgresErrorCodes.CheckViolation, "category_two_level") =>
                "catalog.category-depth-exceeded",
            (PostgresErrorCodes.CheckViolation, "category_parent_not_self") =>
                "catalog.invalid-parent-category",
            (PostgresErrorCodes.ForeignKeyViolation, "category_parent_same_tenant_fk") =>
                "catalog.invalid-parent-category",
            _ => null,
        };
}
