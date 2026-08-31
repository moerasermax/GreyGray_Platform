using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Fulfillment.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────

public readonly record struct ShipmentId(Guid Value)
{
    public static ShipmentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");

    public static bool operator <(ShipmentId left, ShipmentId right) => left.Value.CompareTo(right.Value) < 0;

    public static bool operator >(ShipmentId left, ShipmentId right) => left.Value.CompareTo(right.Value) > 0;

    public static bool operator <=(ShipmentId left, ShipmentId right) => left.Value.CompareTo(right.Value) <= 0;

    public static bool operator >=(ShipmentId left, ShipmentId right) => left.Value.CompareTo(right.Value) >= 0;
}

public readonly record struct PackageId(Guid Value)
{
    public static PackageId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

// ── 列舉 ─────────────────────────────────────────────────────────────────
// DeliveryMethod 定義在 Pricing.Contracts（見那裡的註解說明相依方向）。

public enum ShipmentStatus
{
    Draft = 0,
    Packed = 1,
    Dispatched = 2,
    InTransit = 3,
    ArrivedAtStore = 4,
    Delivered = 5,

    /// <summary>拒收／逾期未取，退回。退回品要開新批號轉現貨庫存（見 docs/03-領域模型.md）。</summary>
    Returned = 6,

    Lost = 7,
}

// ── DTO ──────────────────────────────────────────────────────────────────

/// <summary>
/// 出貨單。<b>Order 與 Shipment 是 N:M</b>——
/// 一張訂單可拆多個包裹；一個包裹也可含同一客人的多張訂單（合併出貨省運費）。
/// 這是日常，不是邊緣案例。
/// </summary>
public sealed record ShipmentSummary(
    ShipmentId Id,
    DeliveryMethod Method,
    ShipmentStatus Status,
    string? TrackingNumber,
    IReadOnlyList<OrderId> OrderIds,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt)
{
    public Money? CarrierCost { get; init; }
}

public sealed record ShipmentPage(IReadOnlyList<ShipmentSummary> Items, string? NextCursor);

public sealed record AdminShipmentListRequest(
    ShipmentStatus? Status,
    ShipmentId? Cursor,
    int Limit = 20);

// ── 同步契約 ─────────────────────────────────────────────────────────────

public interface IFulfillmentQuery
{
    Task<Result<ShipmentSummary>> GetAsync(ShipmentId id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ShipmentSummary>>> GetByOrderAsync(
        OrderId orderId,
        CancellationToken cancellationToken);

    Task<Result<ShipmentPage>> ListAsync(
        AdminShipmentListRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Admin BFF 用的 Fulfillment input port——建立出貨單、交運、簽收。</summary>
public interface IFulfillmentApplication
{
    /// <summary>
    /// 建立出貨單。<b>Order 與 Shipment 是 N:M</b>——<paramref name="orderIds"/>
    /// 可以是同一張訂單被拆進多個包裹裡的其中之一，也可以是同一客人的多張訂單合併出貨。
    /// </summary>
    Task<Result<ShipmentSummary>> CreateAsync(
        IReadOnlyList<OrderId> orderIds,
        DeliveryMethod method,
        CancellationToken cancellationToken);

    /// <summary>
    /// 交運。<paramref name="carrierCost"/> 是<b>付給物流商的成本</b>，
    /// 不是向客人收的運費（那是訂單的 <c>ShippingFee</c>）。
    /// </summary>
    Task<Result<ShipmentSummary>> DispatchAsync(
        ShipmentId id,
        string trackingNumber,
        Money carrierCost,
        CancellationToken cancellationToken);

    /// <summary>
    /// 簽收。正常情況由物流商回報自動觸發，這裡是人工補登用的端點。
    /// </summary>
    Task<Result<ShipmentSummary>> DeliverAsync(
        ShipmentId id,
        CancellationToken cancellationToken);
}

// ── 對外事件 ─────────────────────────────────────────────────────────────

/// <summary>
/// 交運。Ledger 訂閱後開 DR 運費成本 / CR 現金。
/// <see cref="CarrierCost"/> 是<b>你付給物流商的成本</b>，不是你向客人收的運費——
/// 後者在 PricingSnapshot 裡，兩個是獨立的數字，月結時運費是賺是賠自己會浮出來。
/// </summary>
public sealed record ShipmentDispatched(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    ShipmentId ShipmentId,
    IReadOnlyList<OrderId> OrderIds,
    DeliveryMethod Method,
    string TrackingNumber,
    Money CarrierCost)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "fulfillment.ShipmentDispatched.v1";

    public override string AggregateType => "Shipment";

    public override string AggregateId => ShipmentId.ToString();
}

public sealed record ShipmentDelivered(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    ShipmentId ShipmentId,
    IReadOnlyList<OrderId> OrderIds)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "fulfillment.ShipmentDelivered.v1";

    public override string AggregateType => "Shipment";

    public override string AggregateId => ShipmentId.ToString();
}

/// <summary>
/// 拒收退回。Inventory 訂閱後<b>開新批號</b>入庫轉現貨，成本沿用原採購成本。
/// 這是兩種模式唯一交會的地方——也正因為會交會，它們必須共用同一個 Inventory 模組。
/// </summary>
public sealed record ReturnReceived(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    ShipmentId ShipmentId,
    IReadOnlyList<OrderId> OrderIds,
    string Reason)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "fulfillment.ReturnReceived.v1";

    public override string AggregateType => "Shipment";

    public override string AggregateId => ShipmentId.ToString();
}

public sealed record ShipmentLost(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    ShipmentId ShipmentId,
    IReadOnlyList<OrderId> OrderIds,
    CustomerId CustomerId)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "fulfillment.ShipmentLost.v1";

    public override string AggregateType => "Shipment";

    public override string AggregateId => ShipmentId.ToString();
}
