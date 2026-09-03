using GreyGray.Api.Admin;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

/// <summary>
/// BE-44／ADR-032：<c>POST /v1/products/{productId}/skus</c>。
/// 正式機後台建完商品之後，原本沒有任何合法路徑建出第一個 SKU，這一條就是補上的那條路。
/// </summary>
/// <remarks>
/// <para>
/// 這一組跟 <see cref="AdminCancelLineEndpointTests"/> 同款，直接呼叫 Host 抽出來的具名方法：
/// 201／404／400／重播這幾種結果全發生在 endpoint 內部，模組層測試
/// （<c>tests/GreyGray.M1a.IdentityCatalog.Tests</c> 的 <c>CreateSkuAsync</c> 三條）碰不到。
/// </para>
/// <para>
/// <b>ReadOnly → 403 不在這裡</b>：那一段由 <c>AddEndpointFilter(new StaffRoleFilter(...))</c>
/// 在路由層完成，repo 目前沒有能跑完整 admin 路由管線的測試基礎。見 <c>.dispatch/reports/BE-44.md</c>。
/// </para>
/// </remarks>
public sealed class AdminCreateSkuEndpointTests
{
    private const string StaffItem = "greygray.staff-id";

    private static readonly M1aEndpoints.AdminSkuRequest Input = new(
        "經典托特包",
        "米白",
        450,
        new Dimensions(35, 12, 30),
        null,
        null,
        Money.OfMajor(1_280, Currency.TWD),
        null);

    [Fact(DisplayName = "Operator 建立 SKU → 201，available 是 0，商品從此看得到這個 SKU")]
    public async Task Operator_creates_the_first_sku_of_a_product()
    {
        var catalog = new StubCatalogAdministration();
        var context = Context("create-sku");

        var result = await M1aEndpoints.CreateSkuAsync(
            catalog.ProductId.ToString(),
            Input,
            context,
            catalog,
            new EmptyInventory(),
            new InspectableIdempotencyStore(),
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        var body = await ReadBodyAsync(context);
        body.ShouldContain("\"name\":\"經典托特包\"");
        // 新 SKU 一定沒有批號：available 是 0，而且是真的查過庫存拿到的 0。
        body.ShouldContain("\"available\":0");

        var product = (await catalog.GetProductAsync(
            catalog.ProductId,
            TestContext.Current.CancellationToken)).Value;
        var created = product.Skus.ShouldHaveSingleItem();
        created.Name.ShouldBe("經典托特包");
        created.WeightGram.ShouldBe(450);
        created.IsActive.ShouldBeTrue("isActive 沒給時契約的預設是 true。");
        body.ShouldContain($"\"id\":\"{created.Id.Value:N}\"");
    }

    [Fact(DisplayName = "商品不存在 → 404 catalog.product-not-found")]
    public async Task Unknown_product_is_a_404()
    {
        var catalog = new StubCatalogAdministration();
        var context = Context("missing-product");

        var result = await M1aEndpoints.CreateSkuAsync(
            ProductId.New().ToString(),
            Input,
            context,
            catalog,
            new EmptyInventory(),
            new InspectableIdempotencyStore(),
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        (await ReadBodyAsync(context)).ShouldContain("catalog.product-not-found");
    }

    // 路徑參數根本不是 32 字元 GUID 時不必問模組——跟 PATCH /v1/products/{productId} 一致。
    [Fact(DisplayName = "productId 格式不對 → 404 catalog.product-not-found，不打模組")]
    public async Task Malformed_product_id_is_a_404_without_touching_the_module()
    {
        var catalog = new StubCatalogAdministration();
        var context = Context("malformed-product-id");

        var result = await M1aEndpoints.CreateSkuAsync(
            "not-a-guid",
            Input,
            context,
            catalog,
            new EmptyInventory(),
            new InspectableIdempotencyStore(),
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        (await ReadBodyAsync(context)).ShouldContain("catalog.product-not-found");
        catalog.CreateSkuCalls.ShouldBe(0);
    }

    // AdminSkuInput 的必填就是必填：模組回驗證錯誤時是 422，不是幫使用者捏一個預設重量。
    [Fact(DisplayName = "模組回驗證錯誤 → 422，冪等鍵回到可重試")]
    public async Task Validation_failures_are_422()
    {
        var catalog = new StubCatalogAdministration
        {
            Failure = new Error("catalog.sku-invalid", "重量必須大於 0。"),
        };
        var idempotency = new InspectableIdempotencyStore();
        var context = Context("invalid-sku");

        var result = await M1aEndpoints.CreateSkuAsync(
            catalog.ProductId.ToString(),
            Input,
            context,
            catalog,
            new EmptyInventory(),
            idempotency,
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        (await ReadBodyAsync(context)).ShouldContain("catalog.sku-invalid");
        // 什麼都沒建立，同一把 key 改對之後要走得回來。
        idempotency.StatusOf(
            "invalid-sku",
            M1aEndpoints.Scope(context, $"products:{catalog.ProductId}:skus:create"))
            .ShouldBe(InspectableIdempotencyStore.EntryStatus.Abandoned);
    }

    [Fact(DisplayName = "缺 Idempotency-Key → 400 platform.idempotency-key-required")]
    public async Task Missing_idempotency_key_is_a_400()
    {
        var catalog = new StubCatalogAdministration();
        var context = Context(key: null);

        var result = await M1aEndpoints.CreateSkuAsync(
            catalog.ProductId.ToString(),
            Input,
            context,
            catalog,
            new EmptyInventory(),
            new InspectableIdempotencyStore(),
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        (await ReadBodyAsync(context)).ShouldContain("platform.idempotency-key-required");
        catalog.CreateSkuCalls.ShouldBe(0);
    }

    // 前端重送（或使用者連點兩下）不可以變成兩個 SKU——那是後台最難察覺的髒資料。
    [Fact(DisplayName = "同一把 Idempotency-Key 重送 → 重播同一筆 201，只建立一個 SKU")]
    public async Task Same_idempotency_key_creates_one_sku_and_replays_it()
    {
        var catalog = new StubCatalogAdministration();
        var inventory = new EmptyInventory();
        var idempotency = new InspectableIdempotencyStore();
        var staffId = StaffId.New();

        var first = Context("same-key", staffId);
        await (await M1aEndpoints.CreateSkuAsync(
            catalog.ProductId.ToString(),
            Input,
            first,
            catalog,
            inventory,
            idempotency,
            TestContext.Current.CancellationToken)).ExecuteAsync(first);

        var second = Context("same-key", staffId);
        await (await M1aEndpoints.CreateSkuAsync(
            catalog.ProductId.ToString(),
            Input,
            second,
            catalog,
            inventory,
            idempotency,
            TestContext.Current.CancellationToken)).ExecuteAsync(second);

        catalog.CreateSkuCalls.ShouldBe(1);
        first.Response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        second.Response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        (await ReadBodyAsync(second)).ShouldBe(await ReadBodyAsync(first));
    }

    private static DefaultHttpContext Context(string? key, StaffId? staffId = null)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        if (key is not null)
        {
            context.Request.Headers["Idempotency-Key"] = key;
        }

        context.Items[StaffItem] = staffId ?? StaffId.New();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>只做「建 SKU」與「讀回商品」這兩條路，其餘成員一律擲例外。</summary>
    private sealed class StubCatalogAdministration : ICatalogAdministration
    {
        private AdminProductView _product = new(
            ProductId.New(),
            "Test應援周邊",
            null,
            null,
            null,
            FulfillmentMode.Stock,
            [],
            true,
            []);

        public ProductId ProductId => _product.Id;

        public int CreateSkuCalls { get; private set; }

        /// <summary>非 null 時，商品找得到但建立失敗（模擬 <c>ValidateSku</c> 擋下來）。</summary>
        public Error? Failure { get; init; }

        public Task<Result<AdminSkuView>> CreateSkuAsync(
            ProductId productId,
            AdminSkuInput input,
            CancellationToken cancellationToken)
        {
            CreateSkuCalls++;
            if (productId != _product.Id)
            {
                return Task.FromResult(Result<AdminSkuView>.Failure(
                    "catalog.product-not-found",
                    "找不到商品。"));
            }

            if (Failure is not null)
            {
                return Task.FromResult(Result<AdminSkuView>.Failure(Failure));
            }

            var sku = new AdminSkuView(
                SkuId.New(),
                productId,
                input.Name,
                input.VariantName,
                input.WeightGram,
                input.Size,
                input.UnitOfMeasure,
                input.UnitCount,
                input.ListPrice,
                input.IsActive);
            _product = _product with { Skus = [.. _product.Skus, sku] };
            return Task.FromResult(Result<AdminSkuView>.Success(sku));
        }

        public Task<Result<AdminProductView>> GetProductAsync(
            ProductId productId,
            CancellationToken cancellationToken) =>
            Task.FromResult(productId == _product.Id
                ? Result<AdminProductView>.Success(_product)
                : Result<AdminProductView>.Failure("catalog.product-not-found", "找不到商品。"));

        public Task<IReadOnlyList<CategoryView>> ListCategoriesAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<CategoryView>> CreateCategoryAsync(
            CategoryInput input,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<CategoryView>> UpdateCategoryAsync(
            CategoryId categoryId,
            CategoryInput input,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<CursorPage<AdminProductView>>> ListProductsAsync(
            ProductSearch search,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<AdminProductView>> CreateProductAsync(
            AdminProductInput input,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<AdminProductView>> UpdateProductAsync(
            ProductId productId,
            AdminProductInput input,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<AdminSkuView>> UpdateSkuAsync(
            SkuId skuId,
            AdminSkuInput input,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>
    /// 比照 <c>InventoryAvailabilityQuery</c>：沒有任何批號的 SKU 回 <c>0</c>，
    /// 不是漏掉那一列（<see cref="Enumerable.Single{T}(IEnumerable{T})"/> 會炸），也不是失敗。
    /// </summary>
    private sealed class EmptyInventory : IInventoryQuery
    {
        public Task<Result<IReadOnlyList<StockAvailability>>> GetAvailabilityAsync(
            IReadOnlyCollection<SkuId> skuIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<StockAvailability>>.Success(
                skuIds.Select(id => new StockAvailability(id, 0)).ToArray()));

        public Task<Result<Lot>> GetLotAsync(LotId id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
