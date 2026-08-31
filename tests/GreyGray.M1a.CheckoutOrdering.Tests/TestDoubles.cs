using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Checkout.Core;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.Extensions.Logging;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

/// <summary>
/// 把寫出去的 log 留下來。BE-35 用它斷言「退化不准靜默」——
/// 退化回應一定要留下可觀測的痕跡，否則故障就被藏起來了。
/// </summary>
internal sealed class CapturingLogger : ILogger
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _entries.Add((logLevel, formatter(state, exception)));
}

/// <summary>
/// 記憶體版冪等儲存，語意比照 <c>IdempotencyStore</c>：
/// <c>ABANDONED</c> ＋ 同一個 request hash 再來一次會拿到 <c>Proceed</c>，
/// 差別只在這一份<b>留得住最後狀態讓測試看得到</b>。
/// BE-35 從 <c>StorefrontCheckoutEndpointTests</c> 搬到這裡，因為五個端點的迴歸測試都要用。
/// </summary>
internal sealed class InspectableIdempotencyStore : IIdempotencyStore
{
    private readonly Dictionary<(string Key, string Scope), Entry> _entries = [];

    internal enum EntryStatus
    {
        InFlight,
        Completed,
        Abandoned,
    }

    public EntryStatus? StatusOf(string key, string scope) =>
        _entries.TryGetValue((key, scope), out var entry) ? entry.Status : null;

    public Task<(IdempotencyOutcome Outcome, string? cachedResponse)> TryBeginAsync(
        string key,
        string scope,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var identity = (key, scope);
        if (!_entries.TryGetValue(identity, out var entry))
        {
            _entries[identity] = new Entry(requestHash, EntryStatus.InFlight, null);
            return Result(IdempotencyOutcome.Proceed, null);
        }

        if (!StringComparer.Ordinal.Equals(entry.RequestHash, requestHash))
        {
            return Result(IdempotencyOutcome.KeyReusedWithDifferentPayload, null);
        }

        switch (entry.Status)
        {
            case EntryStatus.Abandoned:
                _entries[identity] = entry with { Status = EntryStatus.InFlight, Response = null };
                return Result(IdempotencyOutcome.Proceed, null);
            case EntryStatus.Completed:
                return Result(IdempotencyOutcome.AlreadyCompleted, entry.Response);
            default:
                return Result(IdempotencyOutcome.InFlight, null);
        }
    }

    public Task CompleteAsync(
        string key,
        string scope,
        string responseSnapshot,
        CancellationToken cancellationToken)
    {
        var entry = _entries[(key, scope)];
        _entries[(key, scope)] = entry with
        {
            Status = EntryStatus.Completed,
            Response = responseSnapshot,
        };
        return Task.CompletedTask;
    }

    public Task AbandonAsync(string key, string scope, CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue((key, scope), out var entry))
        {
            _entries[(key, scope)] = entry with
            {
                Status = EntryStatus.Abandoned,
                Response = null,
            };
        }

        return Task.CompletedTask;
    }

    private static Task<(IdempotencyOutcome Outcome, string? cachedResponse)> Result(
        IdempotencyOutcome outcome,
        string? cached) => Task.FromResult((outcome, cached));

    private sealed record Entry(string RequestHash, EntryStatus Status, string? Response);
}

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public DateOnly TodayInTaipei => DateOnly.FromDateTime(UtcNow.AddHours(8).DateTime);
}

internal sealed class FakeCorrelation : ICorrelationContext
{
    public string CorrelationId => "test-correlation";

    public string? CausationId => null;

    public TenantId TenantId => TenantId.Default;
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Saves++;
        return Task.FromResult(1);
    }

    public void Reset() => Saves = 0;
}

internal sealed class FakeEventPublisher : IEventPublisher
{
    public List<object> Published { get; } = [];

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        cancellationToken.ThrowIfCancellationRequested();
        Published.Add(@event);
        return Task.CompletedTask;
    }

    public void Reset() => Published.Clear();
}

internal sealed class FakeCartRepository : ICartRepository
{
    private readonly Dictionary<CartId, Cart> _carts = [];

    public Task<Cart?> GetAsync(
        TenantId tenantId,
        CartId cartId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            _carts.TryGetValue(cartId, out var cart) && cart.TenantId == tenantId
                ? cart
                : null);
    }

    public void Add(Cart cart) => _carts.Add(cart.Id, cart);
}

internal sealed class FakeCatalogQuery(SkuSnapshot sku) : ICatalogQuery
{
    public Task<Result<SkuSnapshot>> GetSkuAsync(
        SkuId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(id == sku.Id
            ? Result<SkuSnapshot>.Success(sku)
            : Result<SkuSnapshot>.Failure("catalog.sku-not-found", "找不到 SKU。"));

    public Task<Result<IReadOnlyList<SkuSnapshot>>> GetSkusAsync(
        IReadOnlyCollection<SkuId> ids,
        CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<SkuSnapshot>>>(
            ids.All(id => id == sku.Id)
                ? new[] { sku }
                : Result<IReadOnlyList<SkuSnapshot>>.Failure(
                    "catalog.sku-not-found",
                    "找不到 SKU。"));
}

internal sealed class FakeCampaignQuery(CampaignOffer offer) : ICampaignQuery
{
    public bool Accepting { get; set; } = true;

    public Task<Result<CampaignSummary>> GetAsync(
        CampaignId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<CampaignSummary>.Failure("campaign.not-found", "找不到團。"));

    public Task<Result<CampaignOffer>> GetOfferAsync(
        CampaignOfferId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(id == offer.Id
            ? Result<CampaignOffer>.Success(offer)
            : Result<CampaignOffer>.Failure("campaign.offer-not-found", "找不到開團商品。"));

    public Task<Result<bool>> IsAcceptingOrdersAsync(
        CampaignId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(id == offer.CampaignId
            ? Result<bool>.Success(Accepting)
            : Result<bool>.Failure("campaign.not-found", "找不到團。"));
}

internal sealed class FakeInventoryQuery(SkuId skuId, int available) : IInventoryQuery
{
    public Task<Result<Lot>> GetLotAsync(LotId id, CancellationToken cancellationToken) =>
        Task.FromResult(Result<Lot>.Failure("inventory.lot-not-found", "找不到批號。"));

    public Task<Result<IReadOnlyList<StockAvailability>>> GetAvailabilityAsync(
        IReadOnlyCollection<SkuId> skuIds,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<StockAvailability>>.Success(
            [new StockAvailability(skuId, available)]));
}

internal sealed class FakePricing(FakeClock clock) : IPricingQuotation
{
    private readonly Dictionary<PricingSnapshotId, PricingSnapshot> _snapshots = [];

    public int QuoteCalls { get; private set; }

    public int FreezeCalls { get; private set; }

    public Task<Result<PricingSnapshot>> QuoteAsync(
        QuoteRequest request,
        CancellationToken cancellationToken)
    {
        QuoteCalls++;
        var snapshot = new PricingSnapshot(
            PricingSnapshotId.New(),
            request.DeliveryMethod,
            request.Lines.Sum(line => line.WeightGram * line.Quantity),
            0,
            request.Lines.Sum(line => line.WeightGram * line.Quantity),
            request.DeliveryMethod switch
            {
                DeliveryMethod.ConvenienceStore => new Money(6_000, Currency.TWD),
                DeliveryMethod.HomeDelivery => new Money(12_000, Currency.TWD),
                _ => Money.Zero(Currency.TWD),
            },
            FeeRuleSetId.New(),
            FeeRuleId.New(),
            ShippingStrategyKind.Flat,
            ["M1a 一口價"],
            clock.UtcNow);
        _snapshots[snapshot.Id] = snapshot;
        return Task.FromResult(Result<PricingSnapshot>.Success(snapshot));
    }

    public Task<Result<PricingSnapshotId>> FreezeAsync(
        PricingSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        FreezeCalls++;
        _snapshots[snapshot.Id] = snapshot;
        return Task.FromResult(Result<PricingSnapshotId>.Success(snapshot.Id));
    }

    public Task<Result<PricingSnapshot>> GetSnapshotAsync(
        PricingSnapshotId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(_snapshots.TryGetValue(id, out var snapshot)
            ? Result<PricingSnapshot>.Success(snapshot)
            : Result<PricingSnapshot>.Failure("pricing.snapshot-not-found", "找不到報價快照。"));

    public void Seed(PricingSnapshot snapshot) => _snapshots[snapshot.Id] = snapshot;
}

internal sealed class FakePaymentQuery : IPaymentQuery
{
    public Task<Result<PaymentSummary>> GetAsync(
        PaymentId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<PaymentSummary>.Failure("payment.not-found", "找不到付款。"));

    public Task<Result<IReadOnlyList<PaymentSummary>>> GetByOrderAsync(
        OrderId orderId,
        CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<PaymentSummary>>>(Array.Empty<PaymentSummary>());

    public Task<Result<IReadOnlyList<ProviderCapability>>> GetEnabledProvidersAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<ProviderCapability>>.Success(
            [new ProviderCapability(PaymentProvider.ECPay, true, true, true)]));
}

internal sealed class FakeCustomerDirectory(CustomerSummary customer) : ICustomerDirectory
{
    public Task<Result<CustomerSummary>> GetAsync(
        CustomerId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(id == customer.Id
            ? Result<CustomerSummary>.Success(customer)
            : Result<CustomerSummary>.Failure("identity.customer-not-found", "找不到會員。"));

    public Task<Result<CustomerContact>> GetContactAsync(
        CustomerId id,
        StaffId? actor,
        string accessReason,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<CustomerContact>.Failure("identity.contact-not-found", "找不到聯絡資料。"));

    public Task<Result<ShippingAddress>> GetAddressAsync(
        AddressId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<ShippingAddress>.Success(
            new ShippingAddress(id, customer.Id, "測試", "0912345678", "100", "台北市", "中正區", "測試路 1 號")));
}

internal sealed class FakeOrderRepository : IOrderRepository
{
    private readonly Dictionary<OrderId, Order> _orders = [];

    public Task<Order?> GetAsync(
        TenantId tenantId,
        OrderId orderId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_orders.TryGetValue(orderId, out var order) && order.TenantId == tenantId
            ? order
            : null);

    public Task<Order?> GetByCheckoutAsync(
        TenantId tenantId,
        CartId cartId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_orders.Values.SingleOrDefault(order =>
            order.TenantId == tenantId && order.CheckoutCartId == cartId));

    public Task<OrderQueryPage> ListCustomerAsync(
        TenantId tenantId,
        CustomerOrderListRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new OrderQueryPage(
            _orders.Values.Where(order => order.TenantId == tenantId
                && order.CustomerId == request.CustomerId
                && (request.Status is null || order.Status == request.Status)).ToArray(),
            null));

    public Task<OrderQueryPage> ListAdminAsync(
        TenantId tenantId,
        AdminOrderListRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new OrderQueryPage(
            _orders.Values.Where(order => order.TenantId == tenantId
                && (request.Status is null || order.Status == request.Status)
                && (request.CampaignId is null
                    || order.Lines.Any(line => line.CampaignId == request.CampaignId))).ToArray(),
            null));

    public Task<IReadOnlyList<Order>> GetByCampaignAsync(
        TenantId tenantId,
        CampaignId campaignId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Order>>(_orders.Values.Where(order =>
            order.TenantId == tenantId
            && order.Lines.Any(line => line.CampaignId == campaignId)).ToArray());

    public Task<Order?> GetByLineAsync(
        TenantId tenantId,
        OrderLineId orderLineId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_orders.Values.SingleOrDefault(order =>
            order.TenantId == tenantId
            && order.Lines.Any(line => line.Id == orderLineId)));

    public void Add(Order order) => _orders.Add(order.Id, order);
}
