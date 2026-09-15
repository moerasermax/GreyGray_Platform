using GreyGray.Api.Storefront;
using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Shared.Kernel;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.IdentityCatalog.Tests;

/// <summary>
/// BE-39：<c>GET /v1/products</c> 與 <c>GET /v1/products/{id}</c> 對預購商品
/// 要把開團資訊填出來——<c>campaignId</c>、<c>campaign</c>、<c>sku.price</c>、
/// <c>sku.campaignOfferId</c> 這四個欄位。
/// </summary>
/// <remarks>
/// <para>
/// 這四個欄位在 <c>ToProductListItem</c>／<c>ToProductDetailAsync</c> 裡<b>直接寫死 <c>null</c></b>，
/// 而 <c>openapi.storefront.yaml</c> 明文規定它們該有值。後果是同一個商品在開團頁可以買
/// NT$1,000，在商品列表卻寫「目前無法購買」、在詳情頁寫「這個規格尚未定價」。
/// <b>它能活這麼久，是因為全 repo 沒有任何一條測試斷言過這四個欄位</b>——
/// 跟 #24（冪等錯誤碼前綴）完全同型。這一組就是那些缺席的斷言。
/// </para>
/// <para>
/// 契約另有一條容易踩反的：<c>Sku.available</c> 對預購<b>恆為 0，但仍然可以下單</b>。
/// 所以這裡也斷言預購根本不去查 <see cref="IInventoryQuery"/>——「不查」比「查到 0」更難退化。
/// </para>
/// </remarks>
public sealed class StorefrontProductEndpointTests
{
    private static readonly DateTimeOffset ClosesAt = new(2026, 9, 20, 16, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "商品列表：預購商品帶出 campaignId，priceFrom 是該團 offer 的售價")]
    public async Task Product_list_carries_the_campaign_id_and_the_campaign_price()
    {
        var scenario = PreorderScenario();

        var page = (await M1aEndpoints.ListProductsAsync(
            Search(),
            null,
            scenario.Catalog,
            scenario.Favorites,
            scenario.Campaigns,
            TestContext.Current.CancellationToken)).Value;

        var item = page.Items.Single();
        item.CampaignId.ShouldBe(scenario.CampaignId);
        item.PriceFrom.ShouldBe(Money.OfMajor(1_000, Currency.TWD));
        item.Mode.ShouldBe(FulfillmentMode.Preorder);
    }

    [Fact(DisplayName = "商品詳情：預購商品附上團的摘要，SKU 帶出該團的定價與 campaignOfferId")]
    public async Task Product_detail_carries_the_campaign_summary_price_and_offer_id()
    {
        var scenario = PreorderScenario();

        var detail = (await M1aEndpoints.GetProductDetailAsync(
            scenario.ProductId,
            null,
            scenario.Catalog,
            scenario.Favorites,
            scenario.Inventory,
            scenario.Campaigns,
            TestContext.Current.CancellationToken)).Value;

        detail.Campaign.ShouldNotBeNull();
        detail.Campaign.Id.ShouldBe(scenario.CampaignId);
        // 前端要顯示截團倒數，也拿 isAcceptingOrders 決定能不能加入購物車。
        detail.Campaign.ClosesAt.ShouldBe(ClosesAt);
        detail.Campaign.IsAcceptingOrders.ShouldBeTrue();

        var sku = detail.Skus.Single();
        sku.Price.ShouldBe(Money.OfMajor(1_000, Currency.TWD));
        sku.CampaignOfferId.ShouldBe(scenario.OfferId);
    }

    // 契約：mode = Preorder 的 SKU available 恆為 0，但仍然可以下單，前端不要拿它擋預購。
    [Fact(DisplayName = "商品詳情：預購的 available 恆為 0，而且根本不去查庫存")]
    public async Task Preorder_availability_stays_zero_without_asking_inventory()
    {
        var scenario = PreorderScenario();

        var detail = (await M1aEndpoints.GetProductDetailAsync(
            scenario.ProductId,
            null,
            scenario.Catalog,
            scenario.Favorites,
            scenario.Inventory,
            scenario.Campaigns,
            TestContext.Current.CancellationToken)).Value;

        detail.Skus.Single().Available.ShouldBe(0);
        scenario.Inventory.Calls.ShouldBe(0);
    }

    // 沒有開著的團時保守留空是對的：那個商品確實買不到。要區分的是「買不到」與
    // 「買得到但後端沒給資料」——後者才是 #26。
    [Fact(DisplayName = "預購商品沒有任何收單中的團時，四個欄位都留空")]
    public async Task Preorder_without_an_open_campaign_stays_null()
    {
        var scenario = PreorderScenario(withOpenCampaign: false);

        var page = (await M1aEndpoints.ListProductsAsync(
            Search(),
            null,
            scenario.Catalog,
            scenario.Favorites,
            scenario.Campaigns,
            TestContext.Current.CancellationToken)).Value;
        var detail = (await M1aEndpoints.GetProductDetailAsync(
            scenario.ProductId,
            null,
            scenario.Catalog,
            scenario.Favorites,
            scenario.Inventory,
            scenario.Campaigns,
            TestContext.Current.CancellationToken)).Value;

        page.Items.Single().CampaignId.ShouldBeNull();
        page.Items.Single().PriceFrom.ShouldBeNull();
        detail.Campaign.ShouldBeNull();
        detail.Skus.Single().Price.ShouldBeNull();
        detail.Skus.Single().CampaignOfferId.ShouldBeNull();
    }

    [Fact(DisplayName = "現貨商品完全不受影響：沒有團、價格是標價、available 走 IInventoryQuery")]
    public async Task Stock_products_are_untouched()
    {
        var scenario = StockScenario();

        var page = (await M1aEndpoints.ListProductsAsync(
            Search(),
            null,
            scenario.Catalog,
            scenario.Favorites,
            scenario.Campaigns,
            TestContext.Current.CancellationToken)).Value;
        var detail = (await M1aEndpoints.GetProductDetailAsync(
            scenario.ProductId,
            null,
            scenario.Catalog,
            scenario.Favorites,
            scenario.Inventory,
            scenario.Campaigns,
            TestContext.Current.CancellationToken)).Value;

        page.Items.Single().CampaignId.ShouldBeNull();
        page.Items.Single().PriceFrom.ShouldBe(Money.OfMajor(780, Currency.TWD));
        detail.Campaign.ShouldBeNull();
        detail.Skus.Single().Price.ShouldBe(Money.OfMajor(780, Currency.TWD));
        detail.Skus.Single().CampaignOfferId.ShouldBeNull();
        detail.Skus.Single().Available.ShouldBe(7);
        scenario.Inventory.Calls.ShouldBe(1);
        // 整頁都是現貨時不該多打 Campaign 一次。
        scenario.Campaigns.Calls.ShouldBe(0);
    }

    // 一個商品可以只有部分規格進團。沒進團的那個規格不能憑空長出價格，
    // 否則客人會看到一個按得下去、但結帳一定失敗的按鈕。
    [Fact(DisplayName = "同一個預購商品裡沒進團的規格不會被塞價格")]
    public async Task Skus_outside_the_campaign_do_not_get_a_price()
    {
        var scenario = PreorderScenario();
        var outsider = new AdminSkuView(
            SkuId.New(),
            scenario.ProductId,
            "韓國雪花秀 潤燥精華液",
            "120ml",
            500,
            new Dimensions(12, 12, 18),
            null,
            null,
            null,
            true);
        scenario.Catalog.AddSku(outsider);

        var detail = (await M1aEndpoints.GetProductDetailAsync(
            scenario.ProductId,
            null,
            scenario.Catalog,
            scenario.Favorites,
            scenario.Inventory,
            scenario.Campaigns,
            TestContext.Current.CancellationToken)).Value;

        var mapped = detail.Skus.Single(sku => sku.Id == outsider.Id);
        mapped.Price.ShouldBeNull();
        mapped.CampaignOfferId.ShouldBeNull();
        detail.Campaign.ShouldNotBeNull();
    }

    // 資料壞掉時不可以退化成「這個商品沒開團」——那跟 #26 的畫面一模一樣，
    // 而看畫面的人分不出「沒開團」與「後端出事了」。
    [Fact(DisplayName = "Campaign 側查詢失敗時要把錯誤帶上來，不可以靜靜地當作沒開團")]
    public async Task Campaign_lookup_failure_is_not_swallowed()
    {
        var scenario = PreorderScenario();
        scenario.Campaigns.FailWith(new Error("catalog.sku-not-found", "找不到 SKU。"));

        var page = await M1aEndpoints.ListProductsAsync(
            Search(),
            null,
            scenario.Catalog,
            scenario.Favorites,
            scenario.Campaigns,
            TestContext.Current.CancellationToken);
        var detail = await M1aEndpoints.GetProductDetailAsync(
            scenario.ProductId,
            null,
            scenario.Catalog,
            scenario.Favorites,
            scenario.Inventory,
            scenario.Campaigns,
            TestContext.Current.CancellationToken);

        page.IsFailure.ShouldBeTrue();
        page.Error.Code.ShouldBe("catalog.sku-not-found");
        detail.IsFailure.ShouldBeTrue();
        detail.Error.Code.ShouldBe("catalog.sku-not-found");
    }

    [Fact(DisplayName = "匿名看商品：isFavorited 為 false，而且完全不查最愛")]
    public async Task Anonymous_products_do_not_query_favorites()
    {
        var scenario = StockScenario();

        var page = (await M1aEndpoints.ListProductsAsync(
            Search(), null, scenario.Catalog, scenario.Favorites, scenario.Campaigns,
            TestContext.Current.CancellationToken)).Value;
        var detail = (await M1aEndpoints.GetProductDetailAsync(
            scenario.ProductId, null, scenario.Catalog, scenario.Favorites,
            scenario.Inventory, scenario.Campaigns, TestContext.Current.CancellationToken)).Value;

        page.Items.Single().IsFavorited.ShouldBeFalse();
        detail.IsFavorited.ShouldBeFalse();
        scenario.Favorites.FindCalls.ShouldBe(0);
    }

    [Fact(DisplayName = "登入看商品：列表與詳情各批次查一次最愛並填入 true")]
    public async Task Signed_in_products_query_favorites_once_per_response()
    {
        var scenario = StockScenario();
        var customerId = CustomerId.New();
        scenario.Favorites.With(scenario.ProductId);

        var page = (await M1aEndpoints.ListProductsAsync(
            Search(), customerId, scenario.Catalog, scenario.Favorites, scenario.Campaigns,
            TestContext.Current.CancellationToken)).Value;
        var detail = (await M1aEndpoints.GetProductDetailAsync(
            scenario.ProductId, customerId, scenario.Catalog, scenario.Favorites,
            scenario.Inventory, scenario.Campaigns, TestContext.Current.CancellationToken)).Value;

        page.Items.Single().IsFavorited.ShouldBeTrue();
        detail.IsFavorited.ShouldBeTrue();
        scenario.Favorites.FindCalls.ShouldBe(2);
    }

    private static ProductSearch Search() => new(null, null, null, false, null, 20);

    private static ProductScenario PreorderScenario(bool withOpenCampaign = true)
    {
        var productId = ProductId.New();
        var skuId = SkuId.New();
        var campaignId = CampaignId.New();
        var offerId = CampaignOfferId.New();
        var catalog = new FakeStorefrontCatalogQuery(
            new StorefrontProductListItem(
                productId,
                "韓國雪花秀 潤燥精華液",
                "首爾連線，回國後統一出貨。",
                "https://example.invalid/sulwhasoo.jpg",
                // Catalog 對預購 SKU 沒有標價，所以 priceFrom 天生是 null——
                // 這正是為什麼列表非得從開團取價不可。
                null,
                null,
                FulfillmentMode.Preorder),
            new StorefrontProductDetail(
                productId,
                "韓國雪花秀 潤燥精華液",
                "潤燥精華液 60ml。",
                "首爾連線，回國後統一出貨。",
                null,
                ["https://example.invalid/sulwhasoo.jpg"],
                FulfillmentMode.Preorder,
                [
                    new AdminSkuView(
                        skuId,
                        productId,
                        "韓國雪花秀 潤燥精華液",
                        "60ml",
                        300,
                        new Dimensions(10, 10, 15),
                        null,
                        null,
                        null,
                        true),
                ]));
        var campaigns = new FakeCampaignStorefront();
        if (withOpenCampaign)
        {
            campaigns.With(new StorefrontProductCampaign(
                productId,
                new StorefrontCampaignListItem(
                    campaignId,
                    "BE-32 test campaign",
                    "首爾",
                    new DateOnly(2026, 9, 25),
                    new DateOnly(2026, 9, 30),
                    ClosesAt,
                    CampaignStatus.Open,
                    true,
                    null),
                Money.OfMajor(1_000, Currency.TWD),
                new Dictionary<SkuId, StorefrontCampaignSkuOffer>
                {
                    [skuId] = new(offerId, campaignId, Money.OfMajor(1_000, Currency.TWD)),
                }));
        }

        return new ProductScenario(
            productId,
            campaignId,
            offerId,
            catalog,
            campaigns,
            new FakeInventoryQuery(skuId, 7),
            new FakeStorefrontFavorites());
    }

    private static ProductScenario StockScenario()
    {
        var productId = ProductId.New();
        var skuId = SkuId.New();
        var listPrice = Money.OfMajor(780, Currency.TWD);
        var catalog = new FakeStorefrontCatalogQuery(
            new StorefrontProductListItem(
                productId,
                "本地現貨面膜",
                "現貨，下單後三日內出貨。",
                "https://example.invalid/mask.jpg",
                listPrice,
                "NT$780／32 顆",
                FulfillmentMode.Stock),
            new StorefrontProductDetail(
                productId,
                "本地現貨面膜",
                "一盒 32 片。",
                "現貨，下單後三日內出貨。",
                null,
                ["https://example.invalid/mask.jpg"],
                FulfillmentMode.Stock,
                [
                    new AdminSkuView(
                        skuId,
                        productId,
                        "本地現貨面膜",
                        null,
                        400,
                        new Dimensions(20, 15, 5),
                        "顆",
                        32,
                        listPrice,
                        true),
                ]));

        return new ProductScenario(
            productId,
            CampaignId.New(),
            CampaignOfferId.New(),
            catalog,
            new FakeCampaignStorefront(),
            new FakeInventoryQuery(skuId, 7),
            new FakeStorefrontFavorites());
    }

    private sealed record ProductScenario(
        ProductId ProductId,
        CampaignId CampaignId,
        CampaignOfferId OfferId,
        FakeStorefrontCatalogQuery Catalog,
        FakeCampaignStorefront Campaigns,
        FakeInventoryQuery Inventory,
        FakeStorefrontFavorites Favorites);
}

internal sealed class FakeStorefrontFavorites : IStorefrontFavorites
{
    private readonly HashSet<ProductId> _productIds = [];

    public int FindCalls { get; private set; }

    public void With(ProductId productId) => _productIds.Add(productId);

    public Task<Result> AddAsync(
        CustomerId customerId,
        ProductId productId,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result> RemoveAsync(
        CustomerId customerId,
        ProductId productId,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result<CursorPage<StorefrontProductListItem>>> ListAsync(
        CustomerId customerId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlySet<ProductId>> FindAsync(
        CustomerId customerId,
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken)
    {
        FindCalls++;
        return Task.FromResult<IReadOnlySet<ProductId>>(
            productIds.Where(_productIds.Contains).ToHashSet());
    }
}

internal sealed class FakeStorefrontCatalogQuery(
    StorefrontProductListItem listItem,
    StorefrontProductDetail detail) : IStorefrontCatalogQuery
{
    private StorefrontProductDetail _detail = detail;

    public void AddSku(AdminSkuView sku) =>
        _detail = _detail with { Skus = [.. _detail.Skus, sku] };

    public Task<IReadOnlyList<CategoryView>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("這一組測試不看分類。");

    public Task<Result<CursorPage<StorefrontProductListItem>>> ListProductsAsync(
        ProductSearch search,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<CursorPage<StorefrontProductListItem>>.Success(
            new CursorPage<StorefrontProductListItem>([listItem], null)));

    public Task<Result<StorefrontProductDetail>> GetProductAsync(
        ProductId productId,
        CancellationToken cancellationToken) =>
        Task.FromResult(productId == _detail.Id
            ? Result<StorefrontProductDetail>.Success(_detail)
            : Result<StorefrontProductDetail>.Failure("catalog.product-not-found", "找不到商品。"));
}

internal sealed class FakeCampaignStorefront : ICampaignStorefront
{
    private readonly Dictionary<ProductId, StorefrontProductCampaign> _pricing = [];
    private Error? _error;

    public int Calls { get; private set; }

    public void With(StorefrontProductCampaign pricing) => _pricing[pricing.ProductId] = pricing;

    public void FailWith(Error error) => _error = error;

    public Task<Result<CampaignPage<StorefrontCampaignListItem>>> ListAsync(
        CampaignPageRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("這一組測試不看開團列表。");

    public Task<Result<StorefrontCampaignDetail>> GetDetailAsync(
        CampaignId id,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("這一組測試不看開團詳情。");

    public Task<Result<IReadOnlyDictionary<ProductId, StorefrontProductCampaign>>>
        FindOpenCampaignPricingAsync(
            IReadOnlyCollection<ProductId> productIds,
            CancellationToken cancellationToken)
    {
        Calls++;
        if (_error is { } error)
        {
            return Task.FromResult(
                Result<IReadOnlyDictionary<ProductId, StorefrontProductCampaign>>.Failure(error));
        }

        return Task.FromResult(
            Result<IReadOnlyDictionary<ProductId, StorefrontProductCampaign>>.Success(
                productIds
                    .Where(_pricing.ContainsKey)
                    .ToDictionary(productId => productId, productId => _pricing[productId])));
    }
}

internal sealed class FakeInventoryQuery(SkuId skuId, int available) : IInventoryQuery
{
    public int Calls { get; private set; }

    public Task<Result<Lot>> GetLotAsync(LotId id, CancellationToken cancellationToken) =>
        throw new NotSupportedException("這一組測試不看批號。");

    public Task<Result<IReadOnlyList<StockAvailability>>> GetAvailabilityAsync(
        IReadOnlyCollection<SkuId> skuIds,
        CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Result<IReadOnlyList<StockAvailability>>.Success(
            skuIds.Select(id => new StockAvailability(id, id == skuId ? available : 0)).ToArray()));
    }
}
