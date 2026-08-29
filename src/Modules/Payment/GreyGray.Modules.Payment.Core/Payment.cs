using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Payment.Core;

internal sealed class Payment
{
    private Payment()
    {
    }

    private Payment(
        PaymentId id,
        TenantId tenantId,
        OrderId orderId,
        PaymentProvider provider,
        Money goodsAmount,
        Money shippingAmount,
        string merchantTradeNo,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        TenantId = tenantId;
        OrderId = orderId;
        Provider = provider;
        Status = PaymentStatus.Pending;
        GoodsAmountMinor = goodsAmount.AmountMinor;
        ShippingAmountMinor = shippingAmount.AmountMinor;
        MerchantTradeNo = merchantTradeNo;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public PaymentId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public OrderId OrderId { get; private set; }

    public PaymentProvider Provider { get; private set; }

    public PaymentStatus Status { get; private set; }

    public Money Amount => new(checked(GoodsAmountMinor + ShippingAmountMinor), Currency.TWD);

    public Money GoodsAmount => new(GoodsAmountMinor, Currency.TWD);

    public Money ShippingAmount => new(ShippingAmountMinor, Currency.TWD);

    public Money? Fee => FeeAmountMinor is null ? null : new Money(FeeAmountMinor.Value, Currency.TWD);

    public Money RefundedAmount => new(RefundedAmountMinor, Currency.TWD);

    public long GoodsAmountMinor { get; private set; }

    public long ShippingAmountMinor { get; private set; }

    public long? FeeAmountMinor { get; private set; }

    public long RefundedAmountMinor { get; private set; }

    public string MerchantTradeNo { get; private set; } = string.Empty;

    public string? ProviderTransactionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset? SettledAt { get; private set; }

    public static Payment Start(
        PaymentId id,
        TenantId tenantId,
        OrderId orderId,
        Money goodsAmount,
        Money shippingAmount,
        string merchantTradeNo,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantTradeNo);
        if (merchantTradeNo.Length > 20)
        {
            throw new InvalidOperationException("綠界特店交易編號不得超過 20 個字元。");
        }

        if (expiresAt <= createdAt)
        {
            throw new InvalidOperationException("付款導轉的到期時間必須晚於建立時間。");
        }

        if (goodsAmount.Currency != Currency.TWD || shippingAmount.Currency != Currency.TWD)
        {
            throw new InvalidOperationException("綠界 M1a 只接受 TWD。");
        }

        if (goodsAmount.IsNegative || shippingAmount.IsNegative || goodsAmount.Add(shippingAmount).IsZero)
        {
            throw new InvalidOperationException("付款金額必須大於零，且貨款與運費不可為負數。");
        }

        return new Payment(
            id,
            tenantId,
            orderId,
            PaymentProvider.ECPay,
            goodsAmount,
            shippingAmount,
            merchantTradeNo,
            createdAt,
            expiresAt);
    }

    public bool SetBreakdown(Money goodsAmount, Money shippingAmount)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException("只有待付款紀錄可以更新貨款與運費拆分。");
        }

        if (goodsAmount.Currency != Currency.TWD || shippingAmount.Currency != Currency.TWD ||
            goodsAmount.IsNegative || shippingAmount.IsNegative ||
            goodsAmount.Add(shippingAmount) != Amount)
        {
            throw new InvalidOperationException("付款拆分必須是非負的新台幣，且加總須等於原應付金額。");
        }

        if (GoodsAmountMinor == goodsAmount.AmountMinor &&
            ShippingAmountMinor == shippingAmount.AmountMinor)
        {
            return false;
        }

        GoodsAmountMinor = goodsAmount.AmountMinor;
        ShippingAmountMinor = shippingAmount.AmountMinor;
        return true;
    }

    public bool Capture(string providerTransactionId, Money fee, DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerTransactionId);
        if (providerTransactionId.Length > 32)
        {
            throw new InvalidOperationException("綠界交易編號不得超過 32 個字元。");
        }

        if (fee.Currency != Currency.TWD || fee.IsNegative)
        {
            throw new InvalidOperationException("綠界手續費必須是非負的新台幣金額。");
        }

        if (Status is PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            if (!StringComparer.Ordinal.Equals(ProviderTransactionId, providerTransactionId))
            {
                throw new InvalidOperationException("同一付款收到不同的綠界交易編號。");
            }

            return false;
        }

        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException($"付款狀態 {Status} 不可轉為 Captured。");
        }

        Status = PaymentStatus.Captured;
        ProviderTransactionId = providerTransactionId;
        FeeAmountMinor = fee.AmountMinor;
        CapturedAt = capturedAt;
        return true;
    }

    public bool Fail(string providerTransactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerTransactionId);
        if (providerTransactionId.Length > 32)
        {
            throw new InvalidOperationException("綠界交易編號不得超過 32 個字元。");
        }

        if (Status == PaymentStatus.Failed)
        {
            if (!StringComparer.Ordinal.Equals(ProviderTransactionId, providerTransactionId))
            {
                throw new InvalidOperationException("同一付款收到不同的綠界交易編號。");
            }

            return false;
        }

        if (Status != PaymentStatus.Pending)
        {
            return false;
        }

        Status = PaymentStatus.Failed;
        ProviderTransactionId = providerTransactionId;
        return true;
    }

    public bool Expire(DateTimeOffset now)
    {
        if (Status == PaymentStatus.Failed)
        {
            return false;
        }

        if (Status != PaymentStatus.Pending || now < ExpiresAt)
        {
            throw new InvalidOperationException("只有已到期的待付款紀錄可以標記為失敗。");
        }

        Status = PaymentStatus.Failed;
        return true;
    }

    public bool Refund(Money amount)
    {
        if (amount.Currency != Currency.TWD || amount.IsNegative || amount.IsZero)
        {
            throw new InvalidOperationException("退款金額必須是大於零的新台幣。");
        }

        if (Status == PaymentStatus.Refunded)
        {
            return false;
        }

        if (Status is not PaymentStatus.Captured and not PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"付款狀態 {Status} 不可執行退款。");
        }

        var next = checked(RefundedAmountMinor + amount.AmountMinor);
        if (next > Amount.AmountMinor)
        {
            throw new InvalidOperationException("累計退款金額不可超過原付款總額。");
        }

        RefundedAmountMinor = next;
        Status = next == Amount.AmountMinor
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        return true;
    }

    public PaymentSummary ToSummary() => new(
        Id,
        OrderId,
        Provider,
        Status,
        Amount,
        Fee,
        ProviderTransactionId,
        CapturedAt,
        SettledAt);
}

internal interface IPaymentRepository
{
    void Add(Payment payment);

    Task<Payment?> FindByOrderAsync(
        TenantId tenantId,
        OrderId orderId,
        CancellationToken cancellationToken);

    Task<Payment?> FindByMerchantTradeNoAsync(
        TenantId tenantId,
        string merchantTradeNo,
        CancellationToken cancellationToken);

    Task<Payment?> FindByIdAsync(
        TenantId tenantId,
        PaymentId id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Payment>> FindByOrderAllAsync(
        TenantId tenantId,
        OrderId orderId,
        CancellationToken cancellationToken);

    Task<Payment?> FindCapturedOrRefundedByOrderAsync(
        TenantId tenantId,
        OrderId orderId,
        CancellationToken cancellationToken);
}
