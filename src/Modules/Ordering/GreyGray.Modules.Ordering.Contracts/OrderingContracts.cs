using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.Modules.Ordering.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────

public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct OrderLineId(Guid Value)
{
    public static OrderLineId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

// ── 列舉 ─────────────────────────────────────────────────────────────────

/// <summary>
/// 訂單狀態。<b>九個狀態，兩種模式共用同一個狀態機</b>——
/// <see cref="PaidAwaitingClose"/> → <see cref="ReadyToShip"/> 那條捷徑就是現貨。
/// </summary>
/// <remarks>
/// 刻意<b>沒有</b> AwaitingCustomerResponse：現場漲價時「逾時視為照買」，
/// 問客人是非阻塞的通知，不會卡住整團。這是設計選擇，不是遺漏。
/// </remarks>
public enum OrderStatus
{
    /// <summary>A 待付款。</summary>
    AwaitingPayment = 0,

    /// <summary>B 已付款・湊團中。</summary>
    PaidAwaitingClose = 1,

    /// <summary>C 已截團・待出發。</summary>
    ClosedAwaitingDeparture = 2,

    /// <summary>D 現場採購中。</summary>
    Purchasing = 3,

    /// <summary>E 已帶回入庫。</summary>
    GoodsReceived = 4,

    /// <summary>F 待出貨。</summary>
    ReadyToShip = 5,

    /// <summary>G 已出貨。</summary>
    Shipped = 6,

    /// <summary>H 已完成（簽收且鑑賞期屆滿）。</summary>
    Completed = 7,

    /// <summary>X 已取消・退款完成。</summary>
    Cancelled = 9,
}

public enum OrderLineStatus
{
    Pending = 0,
    Reserved = 1,
    Purchased = 2,

    /// <summary>現場缺貨。該 line 取消並退款，<b>其餘 line 續行，訂單不整張作廢</b>。</summary>
    Unavailable = 3,

    Shipped = 4,
    Completed = 5,
    Cancelled = 9,
}

/// <summary>
/// 通路擴充接縫 #1：訂單來源。M1 只有 <see cref="Own"/>，
/// 但欄位現在就要有——外部訂單進來無處安放，而且分不出各通路的毛利。
/// </summary>
public enum SourceChannel
{
    Own = 0,
    Line = 1,
    Facebook = 2,
    Instagram = 3,
    Shopee = 4,
}

public enum RefundDestination
{
    /// <summary>原路退回金流商。有手續費。</summary>
    OriginalPaymentMethod = 1,

    /// <summary>退成客戶儲值金。<b>完全不動金流，零手續費</b>。</summary>
    StoredValue = 2,
}

// ── DTO ──────────────────────────────────────────────────────────────────

public sealed record OrderLineView(
    OrderLineId Id,
    SkuId SkuId,
    FulfillmentMode Mode,
    OrderLineStatus Status,
    int Quantity,
    Money UnitPrice,
    CampaignId? CampaignId,
    LotId? ConsumedLot)
{
    public CampaignOfferId? CampaignOfferId { get; init; }

    public Money LineTotal => UnitPrice.MultiplyByQuantity(Quantity);

    public Money? RefundedAmount { get; init; }
}

public sealed record OrderView(
    OrderId Id,
    CustomerId CustomerId,
    SourceChannel Source,
    OrderStatus Status,
    ShippingPolicy ShippingPolicy,
    PricingSnapshotId PricingSnapshotId,
    Money GoodsTotal,
    Money ShippingFee,
    Money GrandTotal,
    IReadOnlyList<OrderLineView> Lines,
    DateTimeOffset PlacedAt)
{
    public string OrderNumber { get; init; } = string.Empty;

    public DeliveryMethod DeliveryMethod { get; init; }

    public AddressId? ShippingAddressId { get; init; }

    public string? ConvenienceStoreCode { get; init; }

    public string? BuyerNote { get; init; }

    public Money? PaidAmount { get; init; }

    public DateTimeOffset? PaymentDueAt { get; init; }

    public string? CancellationReason { get; init; }

    public IReadOnlyList<string> QuoteExplain { get; init; } = [];
}

public sealed record OrderPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record CustomerOrderListRequest(
    CustomerId CustomerId,
    OrderStatus? Status,
    OrderId? Cursor,
    int Limit = 20);

public sealed record AdminOrderListRequest(
    string? Query,
    OrderStatus? Status,
    CampaignId? CampaignId,
    OrderId? Cursor,
    int Limit = 20);

// ── 同步契約 ─────────────────────────────────────────────────────────────

public interface IOrderQuery
{
    Task<Result<OrderView>> GetAsync(OrderId id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<OrderView>>> GetByCampaignAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken);
}

/// <summary>Storefront/Admin BFF 與整合事件 adapter 共用的 Ordering input port。</summary>
public interface IOrderingApplication
{
    /// <summary>以 CartId 與 checkout idempotency key 去重；重送回傳原訂單。</summary>
    Task<Result<OrderView>> CreateFromCheckoutAsync(
        CheckoutCompleted checkout,
        CancellationToken cancellationToken);

    Task<Result<OrderPage<OrderView>>> ListCustomerAsync(
        CustomerOrderListRequest request,
        CancellationToken cancellationToken);

    Task<Result<OrderView>> GetCustomerAsync(
        CustomerId customerId,
        OrderId orderId,
        CancellationToken cancellationToken);

    Task<Result<OrderView>> CancelCustomerAsync(
        CustomerId customerId,
        OrderId orderId,
        string? reason,
        CancellationToken cancellationToken);

    Task<Result<OrderPage<OrderView>>> ListAdminAsync(
        AdminOrderListRequest request,
        CancellationToken cancellationToken);

    Task<Result<OrderView>> GetAdminAsync(
        OrderId orderId,
        CancellationToken cancellationToken);

    Task<Result<OrderView>> CancelAdminAsync(
        OrderId orderId,
        string reason,
        RefundDestination refundTo,
        CancellationToken cancellationToken);

    Task<Result> RecordPaymentCapturedAsync(
        OrderId orderId,
        Money amount,
        CancellationToken cancellationToken);

    Task<Result> RecordPaymentFailedAsync(
        OrderId orderId,
        string failureCode,
        CancellationToken cancellationToken);

    Task<Result> RecordPaymentRefundedAsync(
        OrderId orderId,
        Money amount,
        CancellationToken cancellationToken);
}

// ── 對外事件 ─────────────────────────────────────────────────────────────

public sealed record OrderPlaced(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    OrderId OrderId,
    CustomerId CustomerId,
    SourceChannel Source,
    ShippingPolicy ShippingPolicy,
    PricingSnapshotId PricingSnapshotId,
    Money GoodsTotal,
    Money ShippingFee,
    IReadOnlyList<OrderPlacedLine> Lines)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "ordering.OrderPlaced.v1";

    public override string AggregateType => "Order";

    public override string AggregateId => OrderId.ToString();
}

public sealed record OrderPlacedLine(
    OrderLineId LineId,
    SkuId SkuId,
    FulfillmentMode Mode,
    CampaignId? CampaignId,
    CampaignOfferId? CampaignOfferId,
    int Quantity,
    Money UnitPrice);

/// <summary>付款成功後轉「已付款・湊團中」。</summary>
public sealed record OrderPaid(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    OrderId OrderId,
    Money AmountPaid)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "ordering.OrderPaid.v1";

    public override string AggregateType => "Order";

    public override string AggregateId => OrderId.ToString();
}

/// <summary>Fulfillment 訂閱後開始揀貨打包。</summary>
public sealed record OrderReadyToShip(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    OrderId OrderId,
    DeliveryMethod DeliveryMethod,
    AddressId? ShippingAddressId)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "ordering.OrderReadyToShip.v1";

    public override string AggregateType => "Order";

    public override string AggregateId => OrderId.ToString();
}

/// <summary>簽收且鑑賞期屆滿。Ledger 訂閱後把預收轉收入。</summary>
public sealed record OrderCompleted(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    OrderId OrderId,
    Money GoodsTotal,
    Money ShippingFee)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "ordering.OrderCompleted.v1";

    public override string AggregateType => "Order";

    public override string AggregateId => OrderId.ToString();
}

public sealed record OrderCancelled(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    OrderId OrderId,
    string Reason,
    Money RefundAmount,
    RefundDestination RefundTo)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "ordering.OrderCancelled.v1";

    public override string AggregateType => "Order";

    public override string AggregateId => OrderId.ToString();
}

/// <summary>單一品項取消（現場缺貨）。<b>訂單不整張作廢</b>，其餘 line 續行。</summary>
public sealed record OrderLineCancelled(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    OrderId OrderId,
    OrderLineId LineId,
    string Reason,
    Money RefundAmount,
    RefundDestination RefundTo)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "ordering.OrderLineCancelled.v1";

    public override string AggregateType => "Order";

    public override string AggregateId => OrderId.ToString();
}

/// <summary>Payment 訂閱後去跟金流商建立交易。</summary>
public sealed record PaymentRequested(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    OrderId OrderId,
    CustomerId CustomerId,
    Money Amount,
    string IdempotencyKey)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "ordering.PaymentRequested.v1";

    public override string AggregateType => "Order";

    public override string AggregateId => OrderId.ToString();
}

/// <summary>Payment 訂閱後執行退款（原路或轉儲值金）。</summary>
public sealed record RefundRequested(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    OrderId OrderId,
    OrderLineId? LineId,
    Money Amount,
    RefundDestination Destination,
    string Reason)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "ordering.RefundRequested.v1";

    public override string AggregateType => "Order";

    public override string AggregateId => OrderId.ToString();
}
