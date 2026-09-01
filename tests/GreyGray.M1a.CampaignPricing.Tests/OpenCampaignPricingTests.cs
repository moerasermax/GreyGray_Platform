using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Campaign.Core;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.CampaignPricing.Tests;

/// <summary>
/// BE-39：<see cref="ICampaignStorefront.FindOpenCampaignPricingAsync"/> 的規則。
/// </summary>
/// <remarks>
/// <para>
/// 為什麼要有這一整組：前台商品端點的 <c>campaignId</c>／<c>campaign</c>／<c>price</c>／
/// <c>campaignOfferId</c> 從第一天起就硬編碼成 <c>null</c>，整整活過十幾波，
/// <b>因為全 repo 沒有任何一條測試斷言過它們</b>。這裡釘住資料來源那一側，
/// <c>StorefrontProductEndpointTests</c>（IdentityCatalog.Tests）釘住回應那一側。
/// </para>
/// <para>
/// 這一組刻意不碰資料庫：要釘的是「哪個團贏」「哪個價格贏」這些規則，
/// 而規則全在 <see cref="CampaignService"/> 與 <see cref="CampaignAggregate"/> 裡。
/// </para>
/// </remarks>
public sealed class OpenCampaignPricingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 4, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = TenantId.Default;

    [Fact(DisplayName = "預購商品拿得到它的團、每個 SKU 的 offer，priceFrom 是多 SKU 取最低")]
    public async Task Preorder_product_gets_its_campaign_and_the_lowest_offer_price()
    {
        var product = ProductId.New();
        var cheap = SkuId.New();
        var expensive = SkuId.New();
        var catalog = new FakeCatalogQuery()
            .With(cheap, product)
            .With(expensive, product);
        var campaign = OpenCampaign(
            "首爾秋季連線",
            Now.AddDays(7),
            (cheap, 1_000),
            (expensive, 1_200));
        var service = CreateService(catalog, campaign);

        var result = await service.FindOpenCampaignPricingAsync(
            [product],
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var pricing = result.Value[product];
        pricing.Campaign.Id.ShouldBe(campaign.Id);
        pricing.Campaign.Title.ShouldBe("首爾秋季連線");
        pricing.Campaign.IsAcceptingOrders.ShouldBeTrue();
        pricing.PriceFrom.ShouldBe(Money.OfMajor(1_000, Currency.TWD));
        pricing.Offers.Count.ShouldBe(2);
        pricing.Offers[cheap].SellingPrice.ShouldBe(Money.OfMajor(1_000, Currency.TWD));
        pricing.Offers[expensive].SellingPrice.ShouldBe(Money.OfMajor(1_200, Currency.TWD));
        pricing.Offers[cheap].CampaignId.ShouldBe(campaign.Id);
        pricing.Offers[cheap].OfferId.ShouldBe(OfferIdOf(campaign, cheap));
        pricing.Offers[expensive].OfferId.ShouldBe(OfferIdOf(campaign, expensive));
    }

    // ★ 派工書 §1 指定的規則。campaign.campaign_offer 沒有「一個 SKU 只能屬於一個團」的
    // 唯一鍵，所以這個情境擋不住，只能定規則：取 ClosesAt 最早的那一個。
    // 兩個團的 seed 順序刻意是「晚截團的排前面」——照插入順序取會拿到錯的那個。
    [Fact(DisplayName = "同一個 SKU 掛在兩個收單中的團上時取 ClosesAt 較早的那一個")]
    public async Task Sku_in_two_open_campaigns_takes_the_one_that_closes_first()
    {
        var product = ProductId.New();
        var sku = SkuId.New();
        var catalog = new FakeCatalogQuery().With(sku, product);
        var closesLater = OpenCampaign("十月團", Now.AddDays(30), (sku, 1_500));
        var closesSooner = OpenCampaign("九月團", Now.AddDays(3), (sku, 1_000));
        var service = CreateService(catalog, closesLater, closesSooner);

        var result = await service.FindOpenCampaignPricingAsync(
            [product],
            TestContext.Current.CancellationToken);

        var pricing = result.Value[product];
        pricing.Campaign.Id.ShouldBe(closesSooner.Id);
        pricing.Campaign.Title.ShouldBe("九月團");
        pricing.PriceFrom.ShouldBe(Money.OfMajor(1_000, Currency.TWD));
        pricing.Offers[sku].CampaignId.ShouldBe(closesSooner.Id);
        pricing.Offers[sku].OfferId.ShouldBe(OfferIdOf(closesSooner, sku));
        pricing.Offers[sku].SellingPrice.ShouldBe(Money.OfMajor(1_000, Currency.TWD));
    }

    [Fact(DisplayName = "已截團的團不算數：商品不會出現在結果裡")]
    public async Task Closed_campaign_is_not_open_for_orders()
    {
        var product = ProductId.New();
        var sku = SkuId.New();
        var catalog = new FakeCatalogQuery().With(sku, product);
        var campaign = OpenCampaign("已截團", Now.AddDays(7), (sku, 1_000));
        campaign.Close(Now).IsSuccess.ShouldBeTrue();
        var service = CreateService(catalog, campaign);

        var result = await service.FindOpenCampaignPricingAsync(
            [product],
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    // 狀態還是 Open、但時間已經過了 ClosesAt。這一條守的是「收單與否由 IClock 判斷」，
    // 不是由 status 欄位單獨判斷——截團 Saga 還沒跑到的那段空窗就是這個樣子。
    [Fact(DisplayName = "狀態還是 Open 但已經過了 ClosesAt 的團不算數")]
    public async Task Campaign_past_its_closes_at_is_not_open_for_orders()
    {
        var product = ProductId.New();
        var sku = SkuId.New();
        var catalog = new FakeCatalogQuery().With(sku, product);
        var campaign = OpenCampaign("時間到了", Now.AddHours(1), (sku, 1_000));
        campaign.Status.ShouldBe(CampaignStatus.Open);
        var service = CreateService(catalog, Now.AddHours(2), campaign);

        var result = await service.FindOpenCampaignPricingAsync(
            [product],
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact(DisplayName = "下架的 offer 不算數")]
    public async Task Inactive_offer_is_ignored()
    {
        var product = ProductId.New();
        var removed = SkuId.New();
        var kept = SkuId.New();
        var catalog = new FakeCatalogQuery().With(removed, product).With(kept, product);
        var campaign = OpenCampaign("有下架品項", Now.AddDays(7), (removed, 800), (kept, 1_000));
        campaign.RemoveOffer(OfferIdOf(campaign, removed), hasOrders: false, Now)
            .IsSuccess.ShouldBeTrue();
        var service = CreateService(catalog, campaign);

        var pricing = (await service.FindOpenCampaignPricingAsync(
            [product],
            TestContext.Current.CancellationToken)).Value[product];

        // 下架的那個比較便宜；它要是被算進去，priceFrom 會變成 800。
        pricing.Offers.Keys.ShouldBe([kept]);
        pricing.PriceFrom.ShouldBe(Money.OfMajor(1_000, Currency.TWD));
    }

    [Fact(DisplayName = "沒問到的商品不會被塞回來，問空清單時完全不查")]
    public async Task Only_the_requested_products_come_back()
    {
        var asked = ProductId.New();
        var other = ProductId.New();
        var askedSku = SkuId.New();
        var otherSku = SkuId.New();
        var catalog = new FakeCatalogQuery().With(askedSku, asked).With(otherSku, other);
        var repository = new FakeCampaignRepository(
            OpenCampaign("同一團兩個商品", Now.AddDays(7), (askedSku, 1_000), (otherSku, 2_000)));
        var service = CreateService(catalog, repository, Now);

        var result = await service.FindOpenCampaignPricingAsync(
            [asked],
            TestContext.Current.CancellationToken);

        result.Value.Keys.ShouldBe([asked]);

        var empty = await service.FindOpenCampaignPricingAsync(
            [],
            TestContext.Current.CancellationToken);

        empty.IsSuccess.ShouldBeTrue();
        empty.Value.ShouldBeEmpty();
        repository.ListByStatusCalls.ShouldBe(1, "問空清單時不該去打資料庫");
    }

    // 「查不到 SKU」與「這個商品沒開團」在回應上長得一模一樣（都是價格留空、不能買），
    // 而後者是正常的。所以資料壞掉時要出聲，不可以退化成前者——那正是 #26 的形狀。
    [Fact(DisplayName = "收單中的團掛著查不到的 SKU 時要回失敗，不可以看起來像沒開團")]
    public async Task Missing_catalog_sku_fails_loudly()
    {
        var product = ProductId.New();
        var sku = SkuId.New();
        var service = CreateService(
            new FakeCatalogQuery(),
            OpenCampaign("SKU 被刪掉了", Now.AddDays(7), (sku, 1_000)));

        var result = await service.FindOpenCampaignPricingAsync(
            [product],
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.sku-not-found");
    }

    private static CampaignOfferId OfferIdOf(CampaignAggregate campaign, SkuId skuId) =>
        campaign.Offers.Single(offer => offer.SkuId == skuId).Id;

    private static CampaignAggregate OpenCampaign(
        string title,
        DateTimeOffset closesAt,
        params (SkuId SkuId, decimal Major)[] offers)
    {
        var campaign = CampaignAggregate.CreateDraft(
            CampaignId.New(),
            Tenant,
            new CampaignDraftInput(
                title,
                "首爾",
                new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 8),
                closesAt,
                null,
                null,
                null),
            Now).Value;
        foreach (var (skuId, major) in offers)
        {
            campaign.AddOffer(
                new CampaignOfferInput(skuId, Money.OfMajor(major, Currency.TWD), null),
                Now).IsSuccess.ShouldBeTrue();
        }

        campaign.Publish(Now).IsSuccess.ShouldBeTrue();
        return campaign;
    }

    private static ICampaignStorefront CreateService(
        FakeCatalogQuery catalog,
        params CampaignAggregate[] campaigns) =>
        CreateService(catalog, new FakeCampaignRepository(campaigns), Now);

    private static ICampaignStorefront CreateService(
        FakeCatalogQuery catalog,
        DateTimeOffset now,
        params CampaignAggregate[] campaigns) =>
        CreateService(catalog, new FakeCampaignRepository(campaigns), now);

    private static ICampaignStorefront CreateService(
        FakeCatalogQuery catalog,
        FakeCampaignRepository repository,
        DateTimeOffset now) =>
        new CampaignService(
            repository,
            new CountingUnitOfWork(),
            new NullEventPublisher(),
            catalog,
            new UnusedCampaignOrderQuery(),
            new FixedClock(now),
            new FakeCorrelationContext(Tenant));
}

internal sealed class FakeCampaignRepository(params CampaignAggregate[] campaigns) : ICampaignRepository
{
    public int ListByStatusCalls { get; private set; }

    public Task<CampaignAggregate?> GetAsync(
        CampaignId id,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        Task.FromResult(campaigns.FirstOrDefault(
            campaign => campaign.Id == id && campaign.TenantId == tenantId));

    public Task<CampaignOfferEntity?> GetOfferAsync(
        CampaignOfferId id,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        Task.FromResult(campaigns
            .Where(campaign => campaign.TenantId == tenantId)
            .SelectMany(campaign => campaign.Offers)
            .FirstOrDefault(offer => offer.Id == id));

    public Task<CampaignPageSlice> ListAsync(
        TenantId tenantId,
        CampaignStatus? status,
        long? beforeUtcTicks,
        int limit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("這一組測試不走分頁。");

    /// <summary>比照 <c>CampaignRepository</c>：SQL 只用 tenant ＋ status 過濾，時間規則不在這一層。</summary>
    public Task<IReadOnlyList<CampaignAggregate>> ListByStatusAsync(
        TenantId tenantId,
        CampaignStatus status,
        CancellationToken cancellationToken)
    {
        ListByStatusCalls++;
        return Task.FromResult<IReadOnlyList<CampaignAggregate>>(campaigns
            .Where(campaign => campaign.TenantId == tenantId && campaign.Status == status)
            .ToArray());
    }

    public void Add(CampaignAggregate campaign) =>
        throw new NotSupportedException("這一組測試不新增團。");
}

internal sealed class FakeCatalogQuery : ICatalogQuery
{
    private readonly Dictionary<SkuId, SkuSnapshot> _skus = [];

    public FakeCatalogQuery With(SkuId skuId, ProductId productId)
    {
        _skus[skuId] = new SkuSnapshot(
            skuId,
            productId,
            "韓國雪花秀 潤燥精華液",
            "60ml",
            300,
            new Dimensions(10, 10, 15),
            true);
        return this;
    }

    public Task<Result<SkuSnapshot>> GetSkuAsync(SkuId id, CancellationToken cancellationToken) =>
        Task.FromResult(_skus.TryGetValue(id, out var sku)
            ? sku
            : Result<SkuSnapshot>.Failure("catalog.sku-not-found", "找不到 SKU。"));

    public Task<Result<IReadOnlyList<SkuSnapshot>>> GetSkusAsync(
        IReadOnlyCollection<SkuId> ids,
        CancellationToken cancellationToken)
    {
        var found = new List<SkuSnapshot>(ids.Count);
        foreach (var id in ids)
        {
            if (!_skus.TryGetValue(id, out var sku))
            {
                return Task.FromResult(Result<IReadOnlyList<SkuSnapshot>>.Failure(
                    "catalog.sku-not-found",
                    $"找不到 SKU {id}。"));
            }

            found.Add(sku);
        }

        return Task.FromResult(Result<IReadOnlyList<SkuSnapshot>>.Success(found));
    }
}

internal sealed class UnusedCampaignOrderQuery : ICampaignOrderQuery
{
    public Task<Result<CampaignOrderSnapshot>> GetAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("組前台定價不該去問訂單。");
}

internal sealed class NullEventPublisher : IEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent =>
        throw new NotSupportedException("查詢不該送事件。");
}

internal sealed class FakeCorrelationContext(TenantId tenantId) : ICorrelationContext
{
    public string CorrelationId => "be-39-test";

    public string? CausationId => null;

    public TenantId TenantId => tenantId;
}
