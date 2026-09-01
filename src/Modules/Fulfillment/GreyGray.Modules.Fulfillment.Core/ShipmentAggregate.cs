using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Fulfillment.Core;

internal sealed class ShipmentAggregate
{
    private readonly List<ShipmentOrderLink> _orderLinks = [];

    private ShipmentAggregate()
    {
    }

    private ShipmentAggregate(
        ShipmentId id,
        TenantId tenantId,
        DeliveryMethod method,
        IReadOnlyList<OrderId> orderIds,
        string idempotencyKey,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        Method = method;
        Status = ShipmentStatus.Draft;
        CreationIdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;

        foreach (var orderId in orderIds)
        {
            _orderLinks.Add(new ShipmentOrderLink(Guid.CreateVersion7(), id, tenantId, orderId));
        }
    }

    public ShipmentId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public DeliveryMethod Method { get; private set; }

    public ShipmentStatus Status { get; private set; }

    /// <summary>
    /// 建立這張出貨單時用的冪等鍵。<b>可為 null</b>——`fulfillment.shipment` 在這個欄位
    /// 出現之前就有既有資料列，它們沒有鍵（比照 <c>Cart.CheckoutIdempotencyKey</c>，
    /// 不是比照 <c>Order.CheckoutIdempotencyKey</c> 的必填）。
    /// </summary>
    public string? CreationIdempotencyKey { get; private set; }

    public string? TrackingNumber { get; private set; }

    public long? CarrierCostAmountMinor { get; private set; }

    public Currency? CarrierCostCurrency { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DispatchedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public IReadOnlyList<ShipmentOrderLink> OrderLinks => _orderLinks;

    public IReadOnlyList<OrderId> OrderIds => _orderLinks.Select(link => link.OrderId).ToArray();

    public Money? CarrierCost => CarrierCostAmountMinor is { } amount && CarrierCostCurrency is { } currency
        ? new Money(amount, currency)
        : null;

    /// <summary>
    /// 建立出貨單。<b>Order 與 Shipment 是 N:M</b>——這是日常，不是邊緣案例：
    /// 同一張訂單可以出現在多張出貨單裡（拆多個包裹），<paramref name="orderIds"/>
    /// 也可以一次帶進同一位客人的多張訂單（合併出貨省運費）。
    /// <paramref name="idempotencyKey"/> 是呼叫端帶進來的冪等鍵，
    /// 用來擋掉「同一次動作重送建出第二張」；<b>不是</b>用來擋 N:M 拆包裹。
    /// </summary>
    public static Result<ShipmentAggregate> Create(
        ShipmentId id,
        TenantId tenantId,
        DeliveryMethod method,
        IReadOnlyList<OrderId> orderIds,
        string idempotencyKey,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(orderIds);

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 255)
        {
            return Result<ShipmentAggregate>.Failure(
                "platform.idempotency-key-required",
                "建立出貨單必須提供 1 到 255 字元的 Idempotency-Key。");
        }

        if (orderIds.Count == 0)
        {
            return Result<ShipmentAggregate>.Failure(
                "fulfillment.order-ids-required",
                "出貨單至少要包含一張訂單。");
        }

        if (orderIds.Distinct().Count() != orderIds.Count)
        {
            return Result<ShipmentAggregate>.Failure(
                "fulfillment.duplicate-order-id",
                "同一張訂單不能在同一張出貨單裡重複出現。");
        }

        if (!Enum.IsDefined(method))
        {
            return Result<ShipmentAggregate>.Failure(
                "fulfillment.invalid-delivery-method",
                "配送方式無效。");
        }

        return new ShipmentAggregate(id, tenantId, method, orderIds, idempotencyKey, createdAt);
    }

    /// <summary>
    /// 交運。<paramref name="carrierCost"/> 是<b>付給物流商的成本</b>，
    /// 與向客人收的運費（訂單的 <c>ShippingFee</c>）是兩個獨立的數字。
    /// </summary>
    public Result<DispatchTransition> Dispatch(
        string trackingNumber,
        Money carrierCost,
        DateTimeOffset dispatchedAt)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            return Result<DispatchTransition>.Failure(
                "fulfillment.tracking-number-required",
                "交運必須填寫追蹤號碼。");
        }

        if (carrierCost.IsNegative || !Enum.IsDefined(carrierCost.Currency))
        {
            return Result<DispatchTransition>.Failure(
                "fulfillment.invalid-carrier-cost",
                "物流成本不得為負數，且必須使用已知幣別。");
        }

        if (Status == ShipmentStatus.Dispatched)
        {
            return TrackingNumber == trackingNumber && CarrierCost == carrierCost
                ? DispatchTransition.AlreadyRecorded
                : Result<DispatchTransition>.Failure(
                    "fulfillment.dispatch-already-recorded",
                    "這張出貨單已用不同內容記錄交運結果。");
        }

        if (Status != ShipmentStatus.Draft)
        {
            return Result<DispatchTransition>.Failure(
                "fulfillment.shipment-not-dispatchable",
                "目前的出貨單狀態不能交運。");
        }

        TrackingNumber = trackingNumber;
        CarrierCostAmountMinor = carrierCost.AmountMinor;
        CarrierCostCurrency = carrierCost.Currency;
        Status = ShipmentStatus.Dispatched;
        DispatchedAt = dispatchedAt;
        return DispatchTransition.Recorded;
    }

    /// <summary>
    /// 簽收。<b>送達不等於訂單完成</b>——訂單要等鑑賞期屆滿才轉 Completed，
    /// 那不是這個聚合的職責（見 <c>docs/api/openapi.admin.yaml</c> 的端點說明）。
    /// </summary>
    public Result<DeliverTransition> Deliver(DateTimeOffset deliveredAt)
    {
        if (Status == ShipmentStatus.Delivered)
        {
            return DeliverTransition.AlreadyRecorded;
        }

        if (Status != ShipmentStatus.Dispatched)
        {
            return Result<DeliverTransition>.Failure(
                "fulfillment.shipment-not-deliverable",
                "只有已交運的出貨單可以標記為已送達。");
        }

        Status = ShipmentStatus.Delivered;
        DeliveredAt = deliveredAt;
        return DeliverTransition.Recorded;
    }

    public ShipmentSummary ToContract() =>
        new(
            Id,
            Method,
            Status,
            TrackingNumber,
            OrderIds,
            DispatchedAt,
            DeliveredAt)
        {
            CarrierCost = CarrierCost,
        };
}

internal enum DispatchTransition
{
    AlreadyRecorded = 0,
    Recorded = 1,
}

internal enum DeliverTransition
{
    AlreadyRecorded = 0,
    Recorded = 1,
}

/// <summary>Shipment 與 Order 的 N:M 連接列。</summary>
internal sealed class ShipmentOrderLink
{
    private ShipmentOrderLink()
    {
    }

    public ShipmentOrderLink(Guid id, ShipmentId shipmentId, TenantId tenantId, OrderId orderId)
    {
        Id = id;
        ShipmentId = shipmentId;
        TenantId = tenantId;
        OrderId = orderId;
    }

    public Guid Id { get; private set; }

    public ShipmentId ShipmentId { get; private set; }

    public TenantId TenantId { get; private set; }

    public OrderId OrderId { get; private set; }
}

internal sealed record ShipmentQueryPage(IReadOnlyList<ShipmentAggregate> Items, ShipmentId? NextCursor);

internal interface IShipmentRepository
{
    Task<ShipmentAggregate?> GetAsync(
        TenantId tenantId,
        ShipmentId id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ShipmentAggregate>> GetByOrderAsync(
        TenantId tenantId,
        OrderId orderId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 依建立時的冪等鍵查出貨單，找不到回 null。重播時要回既有那一張的
    /// <c>orderIds</c>，所以實作一定要 <c>Include(OrderLinks)</c>。
    /// </summary>
    Task<ShipmentAggregate?> GetByCreationKeyAsync(
        TenantId tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ShipmentQueryPage> ListAsync(
        TenantId tenantId,
        AdminShipmentListRequest request,
        CancellationToken cancellationToken);

    void Add(ShipmentAggregate shipment);
}
