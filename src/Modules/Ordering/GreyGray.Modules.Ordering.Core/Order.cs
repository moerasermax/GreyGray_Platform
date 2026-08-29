using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Shared.Kernel;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.Modules.Ordering.Core;

internal sealed class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order()
    {
    }

    private Order(
        OrderId id,
        TenantId tenantId,
        CheckoutCompleted checkout,
        PricingSnapshot pricing,
        DateTimeOffset placedAt)
    {
        Id = id;
        TenantId = tenantId;
        CheckoutEventId = checkout.EventId;
        CheckoutCartId = checkout.CartId;
        CheckoutIdempotencyKey = checkout.IdempotencyKey;
        OrderNumber = BuildOrderNumber(id, placedAt);
        CustomerId = checkout.CustomerId;
        Source = SourceChannel.Own;
        Status = OrderStatus.AwaitingPayment;
        ShippingPolicy = checkout.ShippingPolicy;
        DeliveryMethod = checkout.DeliveryMethod;
        ShippingAddressId = checkout.ShippingAddressId;
        ConvenienceStoreCode = checkout.ConvenienceStoreCode;
        BuyerNote = checkout.BuyerNote;
        PricingSnapshotId = checkout.PricingSnapshotId;
        ShippingFeeAmountMinor = pricing.ShippingFee.AmountMinor;
        ShippingFeeCurrency = pricing.ShippingFee.Currency;
        QuoteExplainJson = System.Text.Json.JsonSerializer.Serialize(pricing.Explain);
        PlacedAt = placedAt;

        foreach (var source in checkout.Lines)
        {
            _lines.Add(OrderLine.Create(OrderLineId.New(), id, tenantId, source));
        }

        var goods = SumLines(_lines);
        GoodsTotalAmountMinor = goods.AmountMinor;
        GoodsTotalCurrency = goods.Currency;
        var grand = goods + pricing.ShippingFee;
        GrandTotalAmountMinor = grand.AmountMinor;
        GrandTotalCurrency = grand.Currency;
    }

    public OrderId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public Guid CheckoutEventId { get; private set; }

    public CartId CheckoutCartId { get; private set; }

    public string CheckoutIdempotencyKey { get; private set; } = string.Empty;

    public string OrderNumber { get; private set; } = string.Empty;

    public CustomerId CustomerId { get; private set; }

    public SourceChannel Source { get; private set; }

    public OrderStatus Status { get; private set; }

    public ShippingPolicy ShippingPolicy { get; private set; }

    public DeliveryMethod DeliveryMethod { get; private set; }

    public AddressId? ShippingAddressId { get; private set; }

    public string? ConvenienceStoreCode { get; private set; }

    public string? BuyerNote { get; private set; }

    public PricingSnapshotId PricingSnapshotId { get; private set; }

    public long GoodsTotalAmountMinor { get; private set; }

    public Currency GoodsTotalCurrency { get; private set; }

    public long ShippingFeeAmountMinor { get; private set; }

    public Currency ShippingFeeCurrency { get; private set; }

    public long GrandTotalAmountMinor { get; private set; }

    public Currency GrandTotalCurrency { get; private set; }

    public long? PaidAmountMinor { get; private set; }

    public Currency? PaidCurrency { get; private set; }

    public long RefundedAmountMinor { get; private set; }

    public Currency? RefundedCurrency { get; private set; }

    public string QuoteExplainJson { get; private set; } = "[]";

    public DateTimeOffset PlacedAt { get; private set; }

    public DateTimeOffset? PaymentDueAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public string? LastPaymentFailureCode { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public Money GoodsTotal => new(GoodsTotalAmountMinor, GoodsTotalCurrency);

    public Money ShippingFee => new(ShippingFeeAmountMinor, ShippingFeeCurrency);

    public Money GrandTotal => new(GrandTotalAmountMinor, GrandTotalCurrency);

    public Money? PaidAmount =>
        PaidAmountMinor is not null && PaidCurrency is not null
            ? new Money(PaidAmountMinor.Value, PaidCurrency.Value)
            : null;

    public Money RefundedAmount => new(
        RefundedAmountMinor,
        RefundedCurrency ?? GrandTotal.Currency);

    public static Result<Order> Place(
        OrderId id,
        CheckoutCompleted checkout,
        PricingSnapshot pricing,
        DateTimeOffset placedAt)
    {
        if (checkout.Lines.Count == 0)
        {
            return Result<Order>.Failure("ordering.checkout-empty", "結帳事件沒有任何品項。");
        }

        if (pricing.Id != checkout.PricingSnapshotId
            || pricing.DeliveryMethod != checkout.DeliveryMethod)
        {
            return Result<Order>.Failure(
                "ordering.pricing-snapshot-mismatch",
                "結帳事件與報價快照不一致。");
        }

        if (checkout.Lines.Any(line => line.Quantity is < 1 or > 999 || line.UnitPrice.IsNegative))
        {
            return Result<Order>.Failure("ordering.invalid-checkout-line", "結帳品項的數量或金額無效。");
        }

        try
        {
            return new Order(id, checkout.TenantId, checkout, pricing, placedAt);
        }
        catch (InvalidOperationException)
        {
            return Result<Order>.Failure(
                "ordering.currency-mismatch",
                "商品與運費幣別不一致，無法建立訂單。");
        }
    }

    public Result<PaymentCaptureTransition> CapturePayment(Money amount)
    {
        if (Status is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            return Result<PaymentCaptureTransition>.Failure(
                "ordering.order-not-payable",
                "訂單已取消或完成，不能再付款。");
        }

        if (PaidAmount is not null)
        {
            return PaidAmount.Value == amount
                ? PaymentCaptureTransition.AlreadyRecorded
                : Result<PaymentCaptureTransition>.Failure(
                    "ordering.payment-amount-mismatch",
                    "付款金額與既有付款紀錄不一致。");
        }

        if (Status != OrderStatus.AwaitingPayment || amount != GrandTotal)
        {
            return Result<PaymentCaptureTransition>.Failure(
                "ordering.payment-amount-mismatch",
                "付款金額與訂單應付總額不一致。");
        }

        PaidAmountMinor = amount.AmountMinor;
        PaidCurrency = amount.Currency;
        RefundedCurrency = amount.Currency;
        LastPaymentFailureCode = null;

        var readyToShip = _lines.All(line => line.Mode == FulfillmentMode.Stock);
        Status = readyToShip ? OrderStatus.ReadyToShip : OrderStatus.PaidAwaitingClose;
        return readyToShip
            ? PaymentCaptureTransition.ReadyToShip
            : PaymentCaptureTransition.PaidAwaitingClose;
    }

    public Result RecordPaymentFailure(string failureCode)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            return Result.Success();
        }

        LastPaymentFailureCode = failureCode;
        return Result.Success();
    }

    public Result<Money> CancelByCustomer(string reason, DateTimeOffset cancelledAt)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            return Result<Money>.Failure(
                "ordering.cannot-self-cancel-after-payment",
                "已付款的訂單不能自助取消，請聯絡客服。");
        }

        return Cancel(reason, cancelledAt);
    }

    public Result<Money> CancelByAdmin(string reason, DateTimeOffset cancelledAt)
    {
        if (Status is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            return Result<Money>.Failure(
                "ordering.order-cannot-be-cancelled",
                "訂單已取消或完成，不能再次取消。");
        }

        return Cancel(reason, cancelledAt);
    }

    public Result<Money> CancelLineByAdmin(OrderLineId orderLineId)
    {
        if (Status is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            return Result<Money>.Failure(
                "ordering.order-line-cannot-be-cancelled",
                "訂單已取消或完成，不能再取消品項。");
        }

        var line = _lines.SingleOrDefault(candidate => candidate.Id == orderLineId);
        if (line is null)
        {
            return Result<Money>.Failure(
                "ordering.order-line-not-found",
                "找不到訂單品項。");
        }

        if (line.Status is OrderLineStatus.Unavailable
            or OrderLineStatus.Cancelled
            or OrderLineStatus.Purchased
            or OrderLineStatus.Shipped
            or OrderLineStatus.Completed)
        {
            return Result<Money>.Failure(
                "ordering.order-line-cannot-be-cancelled",
                "目前的訂單品項狀態不能標記為缺貨取消。");
        }

        var refundAmount = line.LineTotal;
        line.MarkUnavailable(refundAmount);
        GoodsTotalAmountMinor = checked(GoodsTotalAmountMinor - refundAmount.AmountMinor);
        GrandTotalAmountMinor = checked(GrandTotalAmountMinor - refundAmount.AmountMinor);
        return refundAmount;
    }

    public Result RecordRefund(Money amount)
    {
        if (amount.IsNegative || PaidAmount is null || amount.Currency != PaidAmount.Value.Currency)
        {
            return Result.Failure("ordering.refund-amount-invalid", "退款金額無效。");
        }

        var next = checked(RefundedAmountMinor + amount.AmountMinor);
        if (next > PaidAmount.Value.AmountMinor)
        {
            return Result.Failure("ordering.refund-exceeds-payment", "退款總額不可超過已付款金額。");
        }

        RefundedAmountMinor = next;
        RefundedCurrency = amount.Currency;
        return Result.Success();
    }

    public Result<PurchaseLineTransition> RecordItemPurchased(
        OrderLineId orderLineId,
        int quantityPurchased)
    {
        var line = _lines.SingleOrDefault(candidate => candidate.Id == orderLineId);
        if (line is null)
        {
            return Result<PurchaseLineTransition>.Failure(
                "ordering.order-line-not-found",
                "找不到訂單品項。");
        }

        if (quantityPurchased < 1 || quantityPurchased > line.Quantity)
        {
            return Result<PurchaseLineTransition>.Failure(
                "ordering.invalid-quantity-purchased",
                "實際買到數量必須介於 1 與訂購數量之間。");
        }

        if (quantityPurchased != line.Quantity)
        {
            return Result<PurchaseLineTransition>.Failure(
                "ordering.partial-purchase-not-supported",
                "短缺數量尚未完成退款前，訂單品項不能標記為全數買到。");
        }

        if (line.Status == OrderLineStatus.Purchased)
        {
            return PurchaseLineTransition.AlreadyRecorded;
        }

        if (line.Status is OrderLineStatus.Unavailable
            or OrderLineStatus.Cancelled
            or OrderLineStatus.Shipped
            or OrderLineStatus.Completed)
        {
            return Result<PurchaseLineTransition>.Failure(
                "ordering.order-line-cannot-be-purchased",
                "目前的訂單品項狀態不能標記為買到。");
        }

        if (Status is OrderStatus.AwaitingPayment or OrderStatus.Cancelled)
        {
            return Result<PurchaseLineTransition>.Failure(
                "ordering.order-not-in-procurement",
                "訂單尚未付款或已取消，不能記錄採購結果。");
        }

        line.MarkPurchased();
        if (Status is OrderStatus.PaidAwaitingClose or OrderStatus.ClosedAwaitingDeparture)
        {
            Status = OrderStatus.Purchasing;
        }

        return PurchaseLineTransition.Recorded;
    }

    public Result<GoodsReceivedTransition> RecordGoodsReceived(
        OrderLineId orderLineId,
        DateTimeOffset receivedAt)
    {
        var line = _lines.SingleOrDefault(candidate => candidate.Id == orderLineId);
        if (line is null)
        {
            return Result<GoodsReceivedTransition>.Failure(
                "ordering.order-line-not-found",
                "找不到訂單品項。");
        }

        if (line.GoodsReceivedAt is not null)
        {
            return GoodsReceivedTransition.AlreadyRecorded;
        }

        if (line.Mode != FulfillmentMode.Preorder)
        {
            return Result<GoodsReceivedTransition>.Failure(
                "ordering.stock-line-does-not-receive-goods",
                "現貨品項不經由預購帶回流程收貨。");
        }

        if (line.Status != OrderLineStatus.Purchased)
        {
            return Result<GoodsReceivedTransition>.Failure(
                "ordering.order-line-not-purchased",
                "只有已買到的預購品項可以記錄收貨。");
        }

        if (Status is OrderStatus.AwaitingPayment or OrderStatus.Cancelled)
        {
            return Result<GoodsReceivedTransition>.Failure(
                "ordering.order-not-in-procurement",
                "訂單尚未付款或已取消，不能記錄收貨。");
        }

        line.MarkGoodsReceived(receivedAt);
        var allReady = _lines.All(candidate =>
            candidate.Mode == FulfillmentMode.Stock
            || candidate.Status is OrderLineStatus.Unavailable
                or OrderLineStatus.Cancelled
                or OrderLineStatus.Shipped
                or OrderLineStatus.Completed
            || candidate.Status == OrderLineStatus.Purchased
                && candidate.GoodsReceivedAt is not null);
        if (!allReady)
        {
            return GoodsReceivedTransition.Recorded;
        }

        Status = OrderStatus.ReadyToShip;
        return GoodsReceivedTransition.ReadyToShip;
    }

    public OrderView ToView() =>
        new(
            Id,
            CustomerId,
            Source,
            Status,
            ShippingPolicy,
            PricingSnapshotId,
            GoodsTotal,
            ShippingFee,
            GrandTotal,
            _lines.Select(line => line.ToView()).ToArray(),
            PlacedAt)
        {
            OrderNumber = OrderNumber,
            DeliveryMethod = DeliveryMethod,
            ShippingAddressId = ShippingAddressId,
            ConvenienceStoreCode = ConvenienceStoreCode,
            BuyerNote = BuyerNote,
            PaidAmount = PaidAmount,
            PaymentDueAt = PaymentDueAt,
            CancellationReason = CancellationReason,
            QuoteExplain = DeserializeExplain(),
        };

    private Result<Money> Cancel(string reason, DateTimeOffset cancelledAt)
    {
        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = cancelledAt;
        foreach (var line in _lines.Where(line => line.Status != OrderLineStatus.Completed))
        {
            line.Cancel();
        }

        return PaidAmount is null ? Money.Zero(GrandTotal.Currency) : GrandTotal;
    }

    private IReadOnlyList<string> DeserializeExplain()
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(QuoteExplainJson) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static Money SumLines(IEnumerable<OrderLine> lines)
    {
        Money? total = null;
        foreach (var line in lines)
        {
            var lineTotal = line.UnitPrice.MultiplyByQuantity(line.Quantity);
            total = total is null ? lineTotal : total.Value + lineTotal;
        }

        return total ?? Money.Zero(Currency.TWD);
    }

    private static string BuildOrderNumber(OrderId id, DateTimeOffset placedAt) =>
        $"GG{placedAt.UtcDateTime:yyMMdd}{id.Value:N}"[..15].ToUpperInvariant();
}

internal enum PaymentCaptureTransition
{
    AlreadyRecorded = 0,
    PaidAwaitingClose = 1,
    ReadyToShip = 2,
}

internal enum PurchaseLineTransition
{
    AlreadyRecorded = 0,
    Recorded = 1,
}

internal enum GoodsReceivedTransition
{
    AlreadyRecorded = 0,
    Recorded = 1,
    ReadyToShip = 2,
}

internal sealed class OrderLine
{
    private OrderLine()
    {
    }

    private OrderLine(
        OrderLineId id,
        OrderId orderId,
        TenantId tenantId,
        CheckoutLine source)
    {
        Id = id;
        OrderId = orderId;
        TenantId = tenantId;
        SkuId = source.SkuId;
        Mode = source.Mode;
        Status = OrderLineStatus.Pending;
        Quantity = source.Quantity;
        UnitPriceAmountMinor = source.UnitPrice.AmountMinor;
        UnitPriceCurrency = source.UnitPrice.Currency;
        CampaignId = source.CampaignId;
        CampaignOfferId = source.CampaignOfferId;
    }

    public OrderLineId Id { get; private set; }

    public OrderId OrderId { get; private set; }

    public TenantId TenantId { get; private set; }

    public SkuId SkuId { get; private set; }

    public FulfillmentMode Mode { get; private set; }

    public OrderLineStatus Status { get; private set; }

    public int Quantity { get; private set; }

    public long UnitPriceAmountMinor { get; private set; }

    public Currency UnitPriceCurrency { get; private set; }

    public CampaignId? CampaignId { get; private set; }

    public CampaignOfferId? CampaignOfferId { get; private set; }

    public LotId? ConsumedLotId { get; private set; }

    public long? RefundedAmountMinor { get; private set; }

    public Currency? RefundedCurrency { get; private set; }

    public DateTimeOffset? GoodsReceivedAt { get; private set; }

    public Money UnitPrice => new(UnitPriceAmountMinor, UnitPriceCurrency);

    public Money LineTotal => UnitPrice.MultiplyByQuantity(Quantity);

    public static OrderLine Create(
        OrderLineId id,
        OrderId orderId,
        TenantId tenantId,
        CheckoutLine source) =>
        new(id, orderId, tenantId, source);

    public void Cancel() => Status = OrderLineStatus.Cancelled;

    public void MarkUnavailable(Money refundAmount)
    {
        if (refundAmount != LineTotal)
        {
            throw new InvalidOperationException("單一品項缺貨必須整條退款。");
        }

        Status = OrderLineStatus.Unavailable;
        RefundedAmountMinor = refundAmount.AmountMinor;
        RefundedCurrency = refundAmount.Currency;
    }

    public void MarkPurchased() => Status = OrderLineStatus.Purchased;

    public void MarkGoodsReceived(DateTimeOffset receivedAt) => GoodsReceivedAt = receivedAt;

    public OrderLineView ToView() =>
        new(Id, SkuId, Mode, Status, Quantity, UnitPrice, CampaignId, ConsumedLotId)
        {
            CampaignOfferId = CampaignOfferId,
            RefundedAmount = RefundedAmountMinor is not null && RefundedCurrency is not null
                ? new Money(RefundedAmountMinor.Value, RefundedCurrency.Value)
                : null,
        };
}

internal sealed record OrderQueryPage(IReadOnlyList<Order> Items, OrderId? NextCursor);

internal interface IOrderRepository
{
    Task<Order?> GetAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken);

    Task<Order?> GetByCheckoutAsync(
        TenantId tenantId,
        CartId cartId,
        CancellationToken cancellationToken);

    Task<OrderQueryPage> ListCustomerAsync(
        TenantId tenantId,
        CustomerOrderListRequest request,
        CancellationToken cancellationToken);

    Task<OrderQueryPage> ListAdminAsync(
        TenantId tenantId,
        AdminOrderListRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetByCampaignAsync(
        TenantId tenantId,
        CampaignId campaignId,
        CancellationToken cancellationToken);

    Task<Order?> GetByLineAsync(
        TenantId tenantId,
        OrderLineId orderLineId,
        CancellationToken cancellationToken);

    void Add(Order order);
}
