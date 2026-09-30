using System.Text.Json;
using GreyGray.Api.Storefront;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Catalog.Infra;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;
using GreyGray.Shared.Kernel.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.IdentityCatalog.Tests;

public sealed partial class IdentityCatalogTests
{
    [Fact(DisplayName = "S1：新增子分類的回傳值保留 ParentId")]
    public async Task Creating_a_child_category_returns_its_parent_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<ICatalogAdministration>();
        var root = await CreateCategoryAsync(admin, "根分類", cancellationToken: cancellationToken);

        var child = await CreateCategoryAsync(
            admin,
            "子分類",
            parentId: root.Id,
            cancellationToken: cancellationToken);

        child.ParentId.ShouldBe(root.Id);
    }

    [Fact(DisplayName = "S2：不存在、跨租戶與自己都不是合法上層，新增與修改回固定 422 碼")]
    public async Task Invalid_parent_categories_return_the_contract_error_code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<ICatalogAdministration>();
        var root = await CreateCategoryAsync(admin, "根分類", cancellationToken: cancellationToken);
        var missing = CategoryId.New();
        var otherTenantCategory = CategoryId.New();
        await ExecuteAsync($"""
            INSERT INTO catalog.category (id, tenant_id, name, sort_order)
            VALUES ('{otherTenantCategory.Value}', '00000000-0000-0000-0000-000000000002', '別租戶', 0);
            """, cancellationToken);

        foreach (var parentId in new[] { missing, otherTenantCategory })
        {
            (await admin.CreateCategoryAsync(
                new CategoryInput("新增", null, 0, parentId),
                cancellationToken)).Error.Code.ShouldBe("catalog.invalid-parent-category");
            (await admin.UpdateCategoryAsync(
                root.Id,
                new CategoryInput("修改", null, 0, parentId),
                cancellationToken)).Error.Code.ShouldBe("catalog.invalid-parent-category");
        }

        (await admin.UpdateCategoryAsync(
            root.Id,
            new CategoryInput("自己", null, 0, root.Id),
            cancellationToken)).Error.Code.ShouldBe("catalog.invalid-parent-category");
    }

    [Fact(DisplayName = "S3：子分類不能再成為上層")]
    public async Task Creating_a_third_level_category_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<ICatalogAdministration>();
        var root = await CreateCategoryAsync(admin, "根", cancellationToken: cancellationToken);
        var child = await CreateCategoryAsync(
            admin, "子", parentId: root.Id, cancellationToken: cancellationToken);

        var result = await admin.CreateCategoryAsync(
            new CategoryInput("孫", null, 0, child.Id), cancellationToken);

        result.Error.Code.ShouldBe("catalog.category-depth-exceeded");
    }

    [Fact(DisplayName = "S4：已經有子分類的根分類不能改成別人的子分類")]
    public async Task A_category_with_children_cannot_become_a_child()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<ICatalogAdministration>();
        var root = await CreateCategoryAsync(admin, "根", cancellationToken: cancellationToken);
        _ = await CreateCategoryAsync(
            admin, "子", parentId: root.Id, cancellationToken: cancellationToken);
        var otherRoot = await CreateCategoryAsync(admin, "另一根", cancellationToken: cancellationToken);

        var result = await admin.UpdateCategoryAsync(
            root.Id,
            new CategoryInput("根", null, 0, otherRoot.Id),
            cancellationToken);

        result.Error.Code.ShouldBe("catalog.category-depth-exceeded");
    }

    [Fact(DisplayName = "S5：PATCH 整筆取代，省略 ParentId 會把子分類升為根分類")]
    public async Task Updating_without_parent_id_replaces_it_with_null()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<ICatalogAdministration>();
        var root = await CreateCategoryAsync(admin, "根", cancellationToken: cancellationToken);
        var child = await CreateCategoryAsync(
            admin, "子", parentId: root.Id, cancellationToken: cancellationToken);

        var updated = await admin.UpdateCategoryAsync(
            child.Id,
            new CategoryInput("子改名", null, 3),
            cancellationToken);

        updated.Value.ParentId.ShouldBeNull();
        (await admin.ListCategoriesAsync(cancellationToken))
            .Single(value => value.Id == child.Id).ParentId.ShouldBeNull();
    }

    [Fact(DisplayName = "S6/S7：只有子分類有可售商品時父子都發布，且清單對父分類封閉")]
    public async Task Published_categories_include_parents_of_sellable_child_categories()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<ICatalogAdministration>();
        var storefront = scope.ServiceProvider.GetRequiredService<IStorefrontCatalogQuery>();
        var parent = await CreateCategoryAsync(admin, "父", cancellationToken: cancellationToken);
        var child = await CreateCategoryAsync(
            admin, "子", parentId: parent.Id, cancellationToken: cancellationToken);
        _ = await AddStockProductAsync(admin, child.Id, "子商品", true, cancellationToken);

        var categories = await storefront.ListCategoriesAsync(cancellationToken);

        categories.Select(value => value.Id).ShouldBe([parent.Id, child.Id], ignoreOrder: true);
        categories.Single(value => value.Id == child.Id).ParentId.ShouldBe(parent.Id);
        foreach (var category in categories.Where(value => value.ParentId is not null))
        {
            categories.ShouldContain(value => value.Id == category.ParentId);
        }
    }

    [Fact(DisplayName = "S8：平面分類的發布條件維持原行為")]
    public async Task Flat_category_visibility_is_unchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<ICatalogAdministration>();
        var storefront = scope.ServiceProvider.GetRequiredService<IStorefrontCatalogQuery>();
        var sellable = await CreateCategoryAsync(admin, "可售", 1, cancellationToken: cancellationToken);
        var empty = await CreateCategoryAsync(admin, "無商品", 2, cancellationToken: cancellationToken);
        var unpriced = await CreateCategoryAsync(admin, "未定價", 3, cancellationToken: cancellationToken);
        _ = await AddStockProductAsync(admin, sellable.Id, "可售商品", true, cancellationToken);
        _ = await AddStockProductAsync(admin, unpriced.Id, "未定價商品", false, cancellationToken);

        var categories = await storefront.ListCategoriesAsync(cancellationToken);

        categories.ShouldHaveSingleItem().Id.ShouldBe(sellable.Id);
        categories.ShouldNotContain(value => value.Id == empty.Id);
        categories.ShouldNotContain(value => value.Id == unpriced.Id);
    }

    [Fact(DisplayName = "S9/S10/S11：includeDescendants 只擴大到直接子分類")]
    public async Task Product_filter_includes_only_direct_children_when_requested()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<ICatalogAdministration>();
        var storefront = scope.ServiceProvider.GetRequiredService<IStorefrontCatalogQuery>();
        var parent = await CreateCategoryAsync(admin, "父", cancellationToken: cancellationToken);
        var child = await CreateCategoryAsync(
            admin, "子", parentId: parent.Id, cancellationToken: cancellationToken);
        var parentProduct = await AddStockProductAsync(admin, parent.Id, "父商品", true, cancellationToken);
        var childProduct = await AddStockProductAsync(admin, child.Id, "子商品", true, cancellationToken);

        var parentWithChildren = await storefront.ListProductsAsync(
            Search(parent.Id, true), cancellationToken);
        var parentOnly = await storefront.ListProductsAsync(
            Search(parent.Id, false), cancellationToken);
        var childWithChildren = await storefront.ListProductsAsync(
            Search(child.Id, true), cancellationToken);
        var childOnly = await storefront.ListProductsAsync(
            Search(child.Id, false), cancellationToken);

        parentWithChildren.Value.Items.Select(value => value.Id)
            .ShouldBe([parentProduct, childProduct], ignoreOrder: true);
        parentOnly.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(parentProduct);
        childWithChildren.Value.Items.Select(value => value.Id)
            .ShouldBe(childOnly.Value.Items.Select(value => value.Id));
        childOnly.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(childProduct);
    }

    [Fact(DisplayName = "S12：商品可直接掛在根分類並出現在前台")]
    public async Task A_product_can_be_published_directly_under_a_root_category()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<ICatalogAdministration>();
        var storefront = scope.ServiceProvider.GetRequiredService<IStorefrontCatalogQuery>();
        var root = await CreateCategoryAsync(admin, "根", cancellationToken: cancellationToken);
        _ = await AddStockProductAsync(admin, root.Id, "根商品", true, cancellationToken);

        (await storefront.ListCategoriesAsync(cancellationToken))
            .ShouldContain(value => value.Id == root.Id);
    }

    private static async Task<CategoryView> CreateCategoryAsync(
        ICatalogAdministration admin,
        string name,
        int sortOrder = 0,
        CategoryId? parentId = null,
        CancellationToken cancellationToken = default) =>
        (await admin.CreateCategoryAsync(
            new CategoryInput(name, null, sortOrder, parentId),
            cancellationToken)).Value;

    private static async Task<ProductId> AddStockProductAsync(
        ICatalogAdministration admin,
        CategoryId categoryId,
        string name,
        bool priced,
        CancellationToken cancellationToken)
    {
        var product = await admin.CreateProductAsync(
            new AdminProductInput(
                name,
                null,
                null,
                categoryId,
                FulfillmentMode.Stock,
                [],
                true),
            cancellationToken);
        product.IsSuccess.ShouldBeTrue();
        var sku = await admin.CreateSkuAsync(
            product.Value.Id,
            new AdminSkuInput(
                $"{name} SKU",
                null,
                1,
                new Dimensions(1, 1, 1),
                "件",
                1,
                priced ? Money.OfMajor(100, Currency.TWD) : null,
                true),
            cancellationToken);
        sku.IsSuccess.ShouldBeTrue();
        return product.Value.Id;
    }

    private static ProductSearch Search(CategoryId categoryId, bool includeDescendants) =>
        new(null, categoryId, null, false, null, 100, includeDescendants);
}

public sealed class CategoryHierarchyContractTests
{
    [Theory(DisplayName = "S13：只有三種 SqlState／constraint 組合會轉成分類 422 碼")]
    [InlineData("23514", "category_two_level", "catalog.category-depth-exceeded")]
    [InlineData("23514", "category_parent_not_self", "catalog.invalid-parent-category")]
    [InlineData("23503", "category_parent_same_tenant_fk", "catalog.invalid-parent-category")]
    [InlineData("40P01", "category_two_level", null)]
    [InlineData("23514", "another_constraint", null)]
    public void PostgreSql_category_errors_are_mapped_selectively(
        string sqlState,
        string constraintName,
        string? expected) =>
        CatalogRepository.MapCategoryHierarchyError(
            Postgres(sqlState, constraintName)).ShouldBe(expected);

    [Fact(DisplayName = "H1：分類 JSON 每筆都有 parentId，根為 null、子為父 id")]
    public async Task Category_projection_writes_parent_id_for_roots_and_children()
    {
        var parentId = CategoryId.New();
        var childId = CategoryId.New();
        var catalog = new CategoryListFake(
            [new(parentId, "父", null, 0), new(childId, "子", null, 1, parentId)]);

        var response = await M1aEndpoints.ProjectCategoriesAsync(
            catalog,
            TestContext.Current.CancellationToken);
        var json = JsonSerializer.Serialize(response, GreyGrayJson.Options);

        json.ShouldContain("\"parentId\":null");
        json.ShouldContain($"\"parentId\":\"{parentId.Value:N}\"");
    }

    [Theory(DisplayName = "H2/H3：includeDescendants true 原樣帶入，不帶時預設 false")]
    [InlineData(true, true)]
    [InlineData(null, false)]
    public void Product_search_binding_preserves_include_descendants(
        bool? input,
        bool expected) =>
        M1aEndpoints.BuildProductSearch(null, null, null, input, null, null)
            .IncludeDescendants.ShouldBe(expected);

    [Fact(DisplayName = "H4：CategoryInput／CategoryView 使用後台同一套 JSON 設定處理 parentId")]
    public void Category_contract_json_round_trips_parent_id_and_writes_null()
    {
        var parentId = CategoryId.New();
        var withParent = JsonSerializer.Deserialize<CategoryInput>(
            $$"""{"name":"x","imageUrl":null,"sortOrder":0,"parentId":"{{parentId.Value:N}}"}""",
            GreyGrayJson.Options);
        var withoutParent = JsonSerializer.Deserialize<CategoryInput>(
            """{"name":"x","imageUrl":null,"sortOrder":0}""",
            GreyGrayJson.Options);

        withParent.ShouldNotBeNull().ParentId.ShouldBe(parentId);
        withoutParent.ShouldNotBeNull().ParentId.ShouldBeNull();
        JsonSerializer.Serialize(
                new CategoryView(CategoryId.New(), "根", null, 0),
                GreyGrayJson.Options)
            .ShouldContain("\"parentId\":null");
    }

    [Theory(DisplayName = "H5：分類階層兩個業務錯誤都是 422 problem+json")]
    [InlineData("catalog.category-depth-exceeded")]
    [InlineData("catalog.invalid-parent-category")]
    public async Task Category_hierarchy_errors_are_unprocessable_entities(string code)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();

        await BffHttp.Problem(new Error(code, "分類階層錯誤。")).ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(
            context.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        context.Response.ContentType.ShouldStartWith("application/problem+json");
        json.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    private static PostgresException Postgres(string sqlState, string constraintName) =>
        new(
            "test",
            "ERROR",
            "ERROR",
            sqlState,
            null,
            null,
            0,
            0,
            null,
            null,
            "catalog",
            "category",
            null,
            null,
            constraintName,
            null,
            null,
            null);

    private sealed class CategoryListFake(IReadOnlyList<CategoryView> categories)
        : IStorefrontCatalogQuery
    {
        public Task<IReadOnlyList<CategoryView>> ListCategoriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(categories);

        public Task<Result<CursorPage<StorefrontProductListItem>>> ListProductsAsync(
            ProductSearch search,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<StorefrontProductDetail>> GetProductAsync(
            ProductId productId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
