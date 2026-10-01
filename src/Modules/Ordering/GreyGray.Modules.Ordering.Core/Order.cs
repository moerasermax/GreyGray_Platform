using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
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
        DateTimeOffset placedAt,
        OrderingPaymentDeadlines deadlines)
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
        ConvenienceStoreName = checkout.ConvenienceStoreName;
        ConvenienceStoreAddress = checkout.ConvenienceStoreAddress;
        RecipientName = checkout.RecipientName;
        RecipientPhone = checkout.RecipientPhone;
        RecipientAddress = checkout.RecipientAddress;
        BuyerNote = checkout.BuyerNote;
        PricingSnapshotId = checkout.PricingSnapshotId;
        ShippingFeeAmountMinor = pricing.ShippingFee.AmountMinor;
        ShippingFeeCurrency = pricing.ShippingFee.Currency;
        QuoteExplainJson = System.Text.Json.JsonSerializer.Serialize(pricing.Explain);
        PlacedAt = placedAt;
        PaymentDueAt = placedAt + deadlines.PaymentDue;
        PaymentAutoCancelAt = PaymentDueAt;

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

    public string? ConvenienceStoreName { get; private set; }

    public string? ConvenienceStoreAddress { get; private set; }

    /// <summary>
    /// 下單當下凍結的收件人姓名（ADR-039，明文）。<b>不會跟著地址簿變動</b>——
    /// 客人事後改地址或刪地址都不影響已成立的訂單（#56）。ADR-039 之前的舊訂單為 null。
    /// </summary>
    public string? RecipientName { get; private set; }

    /// <summary>下單當下凍結的收件人手機（ADR-039，明文）。規則同 <see cref="RecipientName"/>。</summary>
    public string? RecipientPhone { get; private set; }

    /// <summary>
    /// 下單當下凍結的<b>宅配</b>收件地址單行字串（ADR-039，明文）。出貨要用這個——
    /// 後台沒有它以前，宅配訂單在後台看不到地址，等於寄不出去。超商取貨與舊訂單為 null。
    /// </summary>
    public string? RecipientAddress { get; private set; }

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

    public DateTimeOffset? PaymentAutoCancelAt { get; private set; }

    /// <summary>鑑賞期到期時間。訂單掛的出貨單全部簽收後才設定（ADR-025），屆滿轉 <see cref="OrderStatus.Completed"/>。</summary>
    public DateTimeOffset? AppraisalDueAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public OrderCancellationSource? CancellationSource { get; private set; }

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
        DateTimeOffset placedAt,
        OrderingPaymentDeadlines? deadlines = null)
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
            return new Order(
                id,
                checkout.TenantId,
                checkout,
                pricing,
                placedAt,
                deadlines ?? OrderingPaymentDeadlines.Default);
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
        if (Status == OrderStatus.Completed)
        {
            return Result<PaymentCaptureTransition>.Failure(
                "ordering.order-not-payable",
                "訂單已完成，不能再付款。");
        }

        if (PaidAmount is not null)
        {
            return PaidAmount.Value == amount
                ? PaymentCaptureTransition.AlreadyRecorded
                : Result<PaymentCaptureTransition>.Failure(
                    "ordering.payment-amount-mismatch",
                    "付款金額與既有付款紀錄不一致。");
        }

        if (Status is not (OrderStatus.AwaitingPayment or OrderStatus.Cancelled)
            || amount != GrandTotal)
        {
            return Result<PaymentCaptureTransition>.Failure(
                "ordering.payment-amount-mismatch",
                "付款金額與訂單應付總額不一致。");
        }

        PaidAmountMinor = amount.AmountMinor;
        PaidCurrency = amount.Currency;
        LastPaymentFailureCode = null;

        if (Status == OrderStatus.Cancelled)
        {
            return PaymentCaptureTransition.CapturedAfterCancellation;
        }

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
                "這筆訂單目前不能自助取消，如有問題請聯絡客服。");
        }

        return Cancel(reason, cancelledAt, OrderCancellationSource.Customer);
    }

    public Result<Money> CancelByAdmin(string reason, DateTimeOffset cancelledAt)
    {
        if (Status is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            return Result<Money>.Failure(
                "ordering.order-cannot-be-cancelled",
                "訂單已取消或完成，不能再次取消。");
        }

        return Cancel(reason, cancelledAt, OrderCancellationSource.Staff);
    }

    public bool ApplyPaymentInstructions(
        PaymentMethod method,
        DateTimeOffset expiresAt,
        TimeSpan nonCardGrace)
    {
        if (Status != OrderStatus.AwaitingPayment
            || method == PaymentMethod.CreditCard
            || (PaymentDueAt is not null && expiresAt <= PaymentDueAt.Value))
        {
            return false;
        }

        PaymentDueAt = expiresAt;
        PaymentAutoCancelAt = expiresAt + nonCardGrace;
        return true;
    }

    public bool CancelIfPaymentExpired(DateTimeOffset now)
    {
        if (Status != OrderStatus.AwaitingPayment
            || PaymentAutoCancelAt is null
            || now < PaymentAutoCancelAt.Value)
        {
            return false;
        }

        Cancel(
            "逾期未付款，系統自動取消",
            now,
            OrderCancellationSource.PaymentExpired);
        return true;
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

    /// <summary>
    /// 部分買到的短缺數量退款（ADR-026）。買到的數量照常出貨，短缺的數量退款——
    /// 標記買到當下刻意不問退款去向（比照 M1b-2 的決策延後模式），
    /// 客人選好之後才呼叫這裡，這一刻才把 <see cref="OrderLine.Quantity"/> 減下來。
    /// </summary>
    /// <returns>短缺數量的退款金額；已經退過時回 <c>ordering.line-shortfall-already-refunded</c>。</returns>
    public Result<Money> RefundLineShortfallByAdmin(OrderLineId orderLineId)
    {
        if (Status is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            return Result<Money>.Failure(
                "ordering.order-line-shortfall-cannot-be-refunded",
                "訂單已取消或完成，不能再退短缺款。");
        }

        var line = _lines.SingleOrDefault(candidate => candidate.Id == orderLineId);
        if (line is null)
        {
            return Result<Money>.Failure(
                "ordering.order-line-not-found",
                "找不到訂單品項。");
        }

        if (line.Status != OrderLineStatus.Purchased)
        {
            return Result<Money>.Failure(
                "ordering.order-line-shortfall-cannot-be-refunded",
                "只有已記錄買到結果的訂單品項才可能有短缺數量。");
        }

        if (line.QuantityShortfall <= 0)
        {
            return Result<Money>.Failure(
                "ordering.order-line-has-no-shortfall",
                "這個訂單品項全數買到，沒有短缺數量可退。");
        }

        // 冪等：短缺退款是一次性決策，退過就不再退，也不支援反悔改去向。
        if (line.RefundedAmountMinor is not null)
        {
            return Result<Money>.Failure(
                "ordering.line-shortfall-already-refunded",
                "這個訂單品項的短缺數量已經退過款。");
        }

        var refundAmount = line.UnitPrice.MultiplyByQuantity(line.QuantityShortfall);
        line.RecordShortfallRefund(refundAmount);
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

        if (line.Status == OrderLineStatus.Purchased)
        {
            // 冪等要比對內容，不能只看狀態：同一條 line 先記 3 件、後來又送 4 件，
            // 靜默接受會讓短缺數量與退款金額對不上帳。
            // line.Quantity 在短缺退款完成後會被減掉，所以「上次記的買到數量」
            // 要用 Quantity - QuantityShortfall 還原，退款前後都成立。
            return line.Quantity - line.QuantityShortfall == quantityPurchased
                ? PurchaseLineTransition.AlreadyRecorded
                : Result<PurchaseLineTransition>.Failure(
                    "ordering.purchase-already-recorded",
                    "這個訂單品項已用不同的買到數量記錄過採購結果。");
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

        line.MarkPurchased(quantityPurchased);
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

    /// <summary>
    /// Fulfillment 回報「這張訂單掛的出貨單全部簽收」。一張訂單可能對應多個出貨單（N:M），
    /// 呼叫端必須自己先確認全部簽收——這裡只負責狀態轉移與鑑賞期到期時間的冪等記錄。
    /// </summary>
    public ShipmentDeliveryTransition RecordAllShipmentsDelivered(DateTimeOffset appraisalDueAt)
    {
        if (AppraisalDueAt is not null)
        {
            return ShipmentDeliveryTransition.Ignored;
        }

        if (Status != OrderStatus.ReadyToShip)
        {
            return ShipmentDeliveryTransition.Ignored;
        }

        Status = OrderStatus.Shipped;
        AppraisalDueAt = appraisalDueAt;
        return ShipmentDeliveryTransition.AppraisalScheduled;
    }

    /// <summary>
    /// Fulfillment 回報「這張訂單掛的某張出貨單已交運」（#42）。把還在
    /// <see cref="OrderLineStatus.Pending"/> 或 <see cref="OrderLineStatus.Purchased"/> 的品項
    /// 轉成 <see cref="OrderLineStatus.Shipped"/>。
    /// <para>
    /// <see cref="OrderLineStatus.Unavailable"/>（現場缺貨，已整條退款）與
    /// <see cref="OrderLineStatus.Cancelled"/> 一律不動——它們不會出貨。
    /// 已經是 <see cref="OrderLineStatus.Shipped"/>／<see cref="OrderLineStatus.Completed"/> 的也不動：
    /// 一張訂單可能拆進多張出貨單（N:M），第二張交運時不能把已完成的品項倒退回去。
    /// </para>
    /// <para>
    /// <b>訂單本身的 <see cref="Status"/> 不在這裡動。</b>訂單要等掛著的出貨單<b>全部</b>簽收
    /// 才轉 <see cref="OrderStatus.Shipped"/>（<see cref="RecordAllShipmentsDelivered"/>，ADR-025）。
    /// </para>
    /// </summary>
    /// <returns>有沒有任何一條 line 真的被改動——呼叫端據此決定要不要 SaveChanges。</returns>
    public bool MarkLinesShipped()
    {
        var changed = false;
        foreach (var line in _lines.Where(line =>
            line.Status is OrderLineStatus.Pending or OrderLineStatus.Purchased))
        {
            line.MarkShipped();
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// 鑑賞期 Saga Timer 屆滿時呼叫。訂單若在鑑賞期內被取消，狀態已不是
    /// <see cref="OrderStatus.Shipped"/>，這裡會安靜忽略，不會把它拉回 Completed。
    /// </summary>
    public AppraisalTimeoutTransition CompleteAfterAppraisal()
    {
        if (Status != OrderStatus.Shipped || AppraisalDueAt is null)
        {
            return AppraisalTimeoutTransition.Ignored;
        }

        Status = OrderStatus.Completed;
        // 訂單完成，已出貨的品項跟著完成（#42）。Unavailable／Cancelled 不動：
        // 它們沒有出貨，把它們一起標成 Completed 會讓前台顯示成「已完成」而不是「缺貨退款」。
        foreach (var line in _lines.Where(line => line.Status == OrderLineStatus.Shipped))
        {
            line.MarkCompleted();
        }

        return AppraisalTimeoutTransition.Completed;
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
            ConvenienceStoreName = ConvenienceStoreName,
            ConvenienceStoreAddress = ConvenienceStoreAddress,
            RecipientName = RecipientName,
            RecipientPhone = RecipientPhone,
            RecipientAddress = RecipientAddress,
            BuyerNote = BuyerNote,
            PaidAmount = PaidAmount,
            PaymentDueAt = PaymentDueAt,
            CancelledAt = CancelledAt,
            CancellationSource = CancellationSource,
            CancellationReason = CancellationReason,
            QuoteExplain = DeserializeExplain(),
        };

    private Result<Money> Cancel(
        string reason,
        DateTimeOffset cancelledAt,
        OrderCancellationSource source)
    {
        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        CancellationSource = source;
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

    private static string BuildOrderNumber(OrderId id, DateTimeOffset placedAt)
    {
        var randomSuffix = id.Value.ToString("N")[^7..];
        return $"GG{placedAt.UtcDateTime:yyMMdd}{randomSuffix}".ToUpperInvariant();
    }
}

internal enum PaymentCaptureTransition
{
    AlreadyRecorded = 0,
    PaidAwaitingClose = 1,
    ReadyToShip = 2,
    CapturedAfterCancellation = 3,
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

internal enum ShipmentDeliveryTransition
{
    Ignored = 0,
    AppraisalScheduled = 1,
}

internal enum AppraisalTimeoutTransition
{
    Ignored = 0,
    Completed = 1,
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

    /// <summary>
    /// 這條 line <b>現在實際要出貨／已出貨的數量</b>（ADR-026 之後的語意）。
    /// 下單時等於客人訂購的數量；部分買到時仍維持原值不動——
    /// 短缺退款去向還沒決定就先改訂單金額，客人會在退款真的發生之前
    /// 看到總額變小，時序上說不通。<see cref="RecordShortfallRefund"/>
    /// 真的退款那一刻才減掉 <see cref="QuantityShortfall"/>。
    /// </summary>
    public int Quantity { get; private set; }

    /// <summary>
    /// 部分買到時短缺的數量（訂購數量 - 實際買到數量），0 表示沒有短缺。
    /// <b>退款完成後不歸零</b>——「短缺過幾件」是歷史事實；
    /// 「有沒有退過」看 <see cref="RefundedAmountMinor"/> 是不是 null。
    /// </summary>
    public int QuantityShortfall { get; private set; }

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

    /// <summary>
    /// 記錄採購結果。<paramref name="quantityPurchased"/> 小於訂購數量就是部分買到
    /// （ADR-026），差額掛進 <see cref="QuantityShortfall"/> 等退款去向決定，
    /// <see cref="Quantity"/> 這一刻不動。呼叫端（<see cref="Order.RecordItemPurchased"/>）
    /// 已經驗過數量範圍與狀態。
    /// </summary>
    public void MarkPurchased(int quantityPurchased)
    {
        Status = OrderLineStatus.Purchased;
        QuantityShortfall = Quantity - quantityPurchased;
    }

    /// <summary>
    /// 短缺數量的退款完成。退款金額必須剛好等於短缺數量 × 單價——
    /// 比照 <see cref="MarkUnavailable"/> 對 <see cref="LineTotal"/> 的驗證，
    /// 金額對不上是呼叫端算錯，屬於「不該發生」，丟例外不回 Result（鐵則 5）。
    /// </summary>
    public void RecordShortfallRefund(Money refundAmount)
    {
        if (refundAmount != UnitPrice.MultiplyByQuantity(QuantityShortfall))
        {
            throw new InvalidOperationException("短缺退款金額必須等於短缺數量乘上單價。");
        }

        RefundedAmountMinor = refundAmount.AmountMinor;
        RefundedCurrency = refundAmount.Currency;
        Quantity -= QuantityShortfall;
    }

    /// <summary>出貨單交運。呼叫端（<see cref="Order.MarkLinesShipped"/>）已經篩過狀態。</summary>
    public void MarkShipped() => Status = OrderLineStatus.Shipped;

    /// <summary>訂單鑑賞期屆滿完成。呼叫端（<see cref="Order.CompleteAfterAppraisal"/>）已經篩過狀態。</summary>
    public void MarkCompleted() => Status = OrderLineStatus.Completed;

    public void MarkGoodsReceived(DateTimeOffset receivedAt) => GoodsReceivedAt = receivedAt;

    public OrderLineView ToView() =>
        new(Id, SkuId, Mode, Status, Quantity, UnitPrice, CampaignId, ConsumedLotId)
        {
            CampaignOfferId = CampaignOfferId,
            QuantityShortfall = QuantityShortfall,
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
