using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Catalog.Core;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

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
                    product.CategoryId == category.Id &&
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

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}
