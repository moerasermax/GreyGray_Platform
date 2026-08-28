using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Modules.Procurement.Core;
using GreyGray.Modules.Procurement.Infra;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.M1b.Procurement.Tests;

public sealed class ProcurementTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "CampaignClosed 只為已付款預購 line 建清單且 OrderLine 冪等")]
    public async Task Campaign_closed_builds_only_eligible_purchase_items_idempotently()
    {
        var fixture = new ProcurementFixture();
        var eligible = fixture.AddOrder(OrderStatus.PaidAwaitingClose, FulfillmentMode.Preorder);
        fixture.AddOrder(OrderStatus.AwaitingPayment, FulfillmentMode.Preorder);
        fixture.AddOrder(OrderStatus.ReadyToShip, FulfillmentMode.Preorder);
        fixture.AddOrder(OrderStatus.ReadyToShip, FulfillmentMode.Stock);
        var alreadyPurchased = fixture.AddOrder(
            OrderStatus.PaidAwaitingClose,
            FulfillmentMode.Preorder);
        fixture.Orders.Items[^1] = fixture.Orders.Items[^1] with
        {
            Lines = [alreadyPurchased with { Status = OrderLineStatus.Purchased }],
        };

        var first = await fixture.Service.BuildCampaignListAsync(
            fixture.CampaignClosed,
            TestContext.Current.CancellationToken);
        var replay = await fixture.Service.BuildCampaignListAsync(
            fixture.CampaignClosed,
            TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        first.Value.ShouldBe(1);
        replay.Value.ShouldBe(0);
        fixture.Repository.Items.ShouldHaveSingleItem();
        var item = fixture.Repository.Items.Single();
        item.OrderLineId.ShouldBe(eligible.Id);
        item.TargetPrice.ShouldBe(fixture.Offer.TargetPurchasePrice);
        item.QuantityRequested.ShouldBe(eligible.Quantity);
        fixture.UnitOfWork.Saves.ShouldBe(1);
    }

    [Fact(DisplayName = "缺 CampaignOfferId 時整批拒絕，不留下半套採購清單")]
    public async Task Campaign_list_is_all_or_nothing_when_offer_snapshot_is_missing()
    {
        var fixture = new ProcurementFixture();
        fixture.AddOrder(OrderStatus.PaidAwaitingClose, FulfillmentMode.Preorder);
        fixture.Orders.Items.Add(CreateOrder(
            fixture.CampaignId,
            OrderStatus.PaidAwaitingClose,
            CreateLine(fixture.CampaignId, FulfillmentMode.Preorder) with
            {
                CampaignOfferId = null,
            }));

        var result = await fixture.Service.BuildCampaignListAsync(
            fixture.CampaignClosed,
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("procurement.campaign-offer-required");
        fixture.Repository.Items.ShouldBeEmpty();
        fixture.UnitOfWork.Saves.ShouldBe(0);
    }

    [Fact(DisplayName = "標記買到同交易保存成本與 ItemPurchased，重送同內容不再發事件")]
    public async Task Purchased_fact_and_event_are_idempotent()
    {
        var fixture = new ProcurementFixture();
        fixture.AddOrder(OrderStatus.PaidAwaitingClose, FulfillmentMode.Preorder);
        await fixture.Service.BuildCampaignListAsync(
            fixture.CampaignClosed,
            TestContext.Current.CancellationToken);
        fixture.UnitOfWork.Reset();

        var item = fixture.Repository.Items.Single();
        var paid = new MoneyPair(
            Money.OfMajor(1_100, Currency.JPY),
            Money.OfMajor(250, Currency.TWD),
            null);
        var first = await fixture.Service.MarkPurchasedAsync(
            item.Id,
            item.QuantityRequested,
            paid,
            TestContext.Current.CancellationToken);
        var replay = await fixture.Service.MarkPurchasedAsync(
            item.Id,
            item.QuantityRequested,
            paid,
            TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        replay.IsSuccess.ShouldBeTrue();
        first.Value.Status.ShouldBe(PurchaseItemStatus.Purchased);
        first.Value.ActualPaid.ShouldBe(paid);
        first.Value.DecidedAt.ShouldBe(Now);
        fixture.Publisher.Events.ShouldHaveSingleItem();
        var published = fixture.Publisher.Events.Single().ShouldBeOfType<ItemPurchased>();
        published.OrderLineId.ShouldBe(item.OrderLineId);
        published.ActualPaid.ShouldBe(paid);
        fixture.UnitOfWork.Saves.ShouldBe(1);

        var conflict = await fixture.Service.MarkPurchasedAsync(
            item.Id,
            item.QuantityRequested,
            paid with { Booking = Money.OfMajor(260, Currency.TWD) },
            TestContext.Current.CancellationToken);
        conflict.Error.Code.ShouldBe("procurement.purchase-already-recorded");
    }

    [Fact(DisplayName = "部分買到在短缺退款契約完成前明確拒絕，不誤記為 Purchased")]
    public async Task Partial_purchase_is_rejected_until_shortage_compensation_exists()
    {
        var fixture = new ProcurementFixture();
        fixture.AddOrder(OrderStatus.PaidAwaitingClose, FulfillmentMode.Preorder);
        await fixture.Service.BuildCampaignListAsync(
            fixture.CampaignClosed,
            TestContext.Current.CancellationToken);
        var item = fixture.Repository.Items.Single();

        var result = await fixture.Service.MarkPurchasedAsync(
            item.Id,
            item.QuantityRequested - 1,
            new MoneyPair(
                Money.OfMajor(500, Currency.JPY),
                Money.OfMajor(110, Currency.TWD),
                null),
            TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe("procurement.partial-purchase-not-supported");
        item.Status.ShouldBe(PurchaseItemStatus.Pending);
        fixture.Publisher.Events.ShouldBeEmpty();
    }

    [Fact(DisplayName = "記帳成本只接受 TWD，且查詢受 tenant 隔離")]
    public async Task Booking_currency_and_tenant_are_enforced()
    {
        var fixture = new ProcurementFixture();
        fixture.AddOrder(OrderStatus.PaidAwaitingClose, FulfillmentMode.Preorder);
        await fixture.Service.BuildCampaignListAsync(
            fixture.CampaignClosed,
            TestContext.Current.CancellationToken);
        var item = fixture.Repository.Items.Single();

        var invalid = await fixture.Service.MarkPurchasedAsync(
            item.Id,
            item.QuantityRequested,
            new MoneyPair(Money.OfMajor(1_000, Currency.JPY), Money.OfMajor(1_000, Currency.JPY), null),
            TestContext.Current.CancellationToken);
        invalid.Error.Code.ShouldBe("procurement.booking-currency-must-be-twd");

        fixture.Correlation.TenantId = new TenantId(Guid.CreateVersion7());
        var hidden = await fixture.Service.MarkPurchasedAsync(
            item.Id,
            item.QuantityRequested,
            new MoneyPair(Money.OfMajor(1_000, Currency.JPY), Money.OfMajor(220, Currency.TWD), null),
            TestContext.Current.CancellationToken);
        hidden.Error.Code.ShouldBe("procurement.purchase-item-not-found");
    }

    [Fact(DisplayName = "Procurement EF model 與 Outbox 共用 DbContext")]
    public void Ef_model_contains_procurement_and_platform_tables()
    {
        using var dbContext = new ProcurementDbContext(
            new DbContextOptionsBuilder<ProcurementDbContext>()
                .UseNpgsql("Host=localhost;Database=greygray;Username=greygray;Password=greygray")
                .Options);

        dbContext.Model.FindEntityType(typeof(PurchaseItemAggregate))!
            .GetSchema().ShouldBe("procurement");
        dbContext.Model.FindEntityType(typeof(PurchaseItemAggregate))!
            .GetTableName().ShouldBe("purchase_item");
        dbContext.Model.FindEntityType(typeof(OutboxMessage))!
            .GetSchema().ShouldBe("platform");
    }

    [Fact(DisplayName = "Infra 只公開組合根且沒有連線字串時仍可完成 lazy registration")]
    public void Infra_exports_only_registration_and_registers_lazily()
    {
        typeof(ProcurementModuleRegistration).Assembly.GetExportedTypes()
            .ShouldBe([typeof(ProcurementModuleRegistration)], ignoreOrder: true);

        var services = new ServiceCollection();
        services.AddProcurementModule(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        provider.ShouldNotBeNull();
    }

    private static OrderView CreateOrder(
        CampaignId campaignId,
        OrderStatus status,
        params OrderLineView[] lines) =>
        new(
            OrderId.New(),
            CustomerId.New(),
            SourceChannel.Own,
            status,
            ShippingPolicy.HoldUntilComplete,
            PricingSnapshotId.New(),
            Money.OfMajor(1_000, Currency.TWD),
            Money.OfMajor(60, Currency.TWD),
            Money.OfMajor(1_060, Currency.TWD),
            lines,
            Now)
        {
            OrderNumber = $"TEST{campaignId.Value:N}"[..15],
            DeliveryMethod = DeliveryMethod.ConvenienceStore,
            PaidAmount = status == OrderStatus.AwaitingPayment
                ? null
                : Money.OfMajor(1_060, Currency.TWD),
        };

    private static OrderLineView CreateLine(
        CampaignId campaignId,
        FulfillmentMode mode) =>
        new(
            OrderLineId.New(),
            SkuId.New(),
            mode,
            OrderLineStatus.Pending,
            2,
            Money.OfMajor(500, Currency.TWD),
            mode == FulfillmentMode.Preorder ? campaignId : null,
            null)
        {
            CampaignOfferId = mode == FulfillmentMode.Preorder
                ? CampaignOfferId.New()
                : null,
        };

    private sealed class ProcurementFixture
    {
        public ProcurementFixture()
        {
            CampaignId = CampaignId.New();
            var offerId = CampaignOfferId.New();
            var skuId = SkuId.New();
            Offer = new CampaignOffer(
                offerId,
                CampaignId,
                skuId,
                Money.OfMajor(500, Currency.TWD),
                Money.OfMajor(350, Currency.TWD),
                true);
            CampaignClosed = new CampaignClosed(
                Guid.CreateVersion7(),
                Now,
                TenantId.Default,
                CampaignId);
            Repository = new MemoryProcurementRepository();
            UnitOfWork = new CountingUnitOfWork();
            Publisher = new RecordingPublisher();
            Orders = new FakeOrderQuery();
            Campaigns = new FakeCampaignQuery(Offer);
            Correlation = new MutableCorrelation { TenantId = TenantId.Default };
            Service = new ProcurementApplicationService(
                Repository,
                UnitOfWork,
                Publisher,
                Orders,
                Campaigns,
                new FixedClock(Now),
                Correlation);
        }

        public CampaignId CampaignId { get; }

        public CampaignOffer Offer { get; }

        public CampaignClosed CampaignClosed { get; }

        public MemoryProcurementRepository Repository { get; }

        public CountingUnitOfWork UnitOfWork { get; }

        public RecordingPublisher Publisher { get; }

        public FakeOrderQuery Orders { get; }

        public FakeCampaignQuery Campaigns { get; }

        public MutableCorrelation Correlation { get; }

        public ProcurementApplicationService Service { get; }

        public OrderLineView AddOrder(OrderStatus status, FulfillmentMode mode)
        {
            var line = CreateLine(CampaignId, mode);
            if (mode == FulfillmentMode.Preorder)
            {
                line = line with { SkuId = Offer.SkuId };
                line.CampaignOfferId.ShouldNotBeNull();
                Campaigns.Aliases[line.CampaignOfferId!.Value] = Offer;
            }

            Orders.Items.Add(CreateOrder(CampaignId, status, line));
            return line;
        }
    }
}

internal sealed class MemoryProcurementRepository : IProcurementRepository
{
    public List<PurchaseItemAggregate> Items { get; } = [];

    public Task<PurchaseItemAggregate?> GetAsync(
        TenantId tenantId,
        PurchaseItemId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(item => item.TenantId == tenantId && item.Id == id));

    public Task<IReadOnlyList<PurchaseItemAggregate>> GetCampaignListAsync(
        TenantId tenantId,
        CampaignId campaignId,
        bool tracking,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PurchaseItemAggregate>>(Items.Where(item =>
            item.TenantId == tenantId && item.CampaignId == campaignId).ToArray());

    public void Add(PurchaseItemAggregate item) => Items.Add(item);
}

internal sealed class FakeOrderQuery : IOrderQuery
{
    public List<OrderView> Items { get; } = [];

    public Task<Result<OrderView>> GetAsync(
        OrderId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(order => order.Id == id) is { } order
            ? Result<OrderView>.Success(order)
            : Result<OrderView>.Failure("ordering.order-not-found", "找不到訂單。"));

    public Task<Result<IReadOnlyList<OrderView>>> GetByCampaignAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<OrderView>>.Success(Items.Where(order =>
            order.Lines.Any(line => line.CampaignId == campaignId)).ToArray()));
}

internal sealed class FakeCampaignQuery(CampaignOffer primary) : ICampaignQuery
{
    public Dictionary<CampaignOfferId, CampaignOffer> Aliases { get; } =
        new() { [primary.Id] = primary };

    public Task<Result<CampaignSummary>> GetAsync(
        CampaignId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<CampaignSummary>.Failure("campaign.not-found", "找不到開團。"));

    public Task<Result<CampaignOffer>> GetOfferAsync(
        CampaignOfferId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(Aliases.TryGetValue(id, out var offer)
            ? Result<CampaignOffer>.Success(offer)
            : Result<CampaignOffer>.Failure("campaign.offer-not-found", "找不到開團商品。"));

    public Task<Result<bool>> IsAcceptingOrdersAsync(
        CampaignId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<bool>.Success(false));
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.FromResult(1);
    }

    public void Reset() => Saves = 0;
}

internal sealed class RecordingPublisher : IEventPublisher
{
    public List<object> Events { get; } = [];

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        Events.Add(@event);
        return Task.CompletedTask;
    }
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;

    public DateOnly TodayInTaipei => DateOnly.FromDateTime(now.AddHours(8).DateTime);
}

internal sealed class MutableCorrelation : ICorrelationContext
{
    public string CorrelationId => "m1b-procurement-test";

    public string? CausationId => null;

    public TenantId TenantId { get; set; }
}
