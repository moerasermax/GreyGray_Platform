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
        DateTimeOffset expiresAt,
        PaymentMethod method)
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
        Method = method;
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

    public PaymentMethod? Method { get; private set; }

    public string? BankCode { get; private set; }

    public string? VirtualAccount { get; private set; }

    public string? PaymentNo { get; private set; }

    public string? Barcode1 { get; private set; }

    public string? Barcode2 { get; private set; }

    public string? Barcode3 { get; private set; }

    public DateTimeOffset? ProviderExpiresAt { get; private set; }

    public DateTimeOffset? InstructionsIssuedAt { get; private set; }

    public string? LateCaptureTradeNo { get; private set; }

    public DateTimeOffset? LateCapturedAt { get; private set; }

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
        DateTimeOffset expiresAt,
        PaymentMethod method = PaymentMethod.CreditCard)
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
            expiresAt,
            method);
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

    public Result<CaptureTransition> Capture(
        string providerTransactionId,
        Money fee,
        DateTimeOffset capturedAt)
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
                return Result<CaptureTransition>.Failure(
                    "payment.provider-trade-no-mismatch",
                    "同一付款收到不同的綠界交易編號。");
            }

            return CaptureTransition.AlreadyCaptured;
        }

        if (Status == PaymentStatus.Failed)
        {
            if (LateCaptureTradeNo is null)
            {
                LateCaptureTradeNo = providerTransactionId;
                LateCapturedAt = capturedAt;
                return CaptureTransition.LateCaptureRecorded;
            }

            return StringComparer.Ordinal.Equals(LateCaptureTradeNo, providerTransactionId)
                ? CaptureTransition.LateCaptureAlreadyRecorded
                : Result<CaptureTransition>.Failure(
                    "payment.late-capture-trade-no-mismatch",
                    "同一筆失效付款已記錄另一個晚到的綠界交易編號。");
        }

        if (Status == PaymentStatus.InstructionsIssued &&
            !StringComparer.Ordinal.Equals(ProviderTransactionId, providerTransactionId))
        {
            return Result<CaptureTransition>.Failure(
                "payment.instructions-trade-no-mismatch",
                "付款完成通知的綠界交易編號與取號通知不一致。");
        }

        if (Status is not PaymentStatus.Pending and not PaymentStatus.InstructionsIssued)
        {
            return Result<CaptureTransition>.Failure(
                "payment.invalid-capture-transition",
                $"付款狀態 {Status} 不可轉為 Captured。");
        }

        Status = PaymentStatus.Captured;
        ProviderTransactionId = providerTransactionId;
        FeeAmountMinor = fee.AmountMinor;
        CapturedAt = capturedAt;
        return CaptureTransition.Captured;
    }

    public Result<bool> Fail(string providerTransactionId)
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
                return Result<bool>.Failure(
                    "payment.provider-trade-no-mismatch",
                    "同一付款收到不同的綠界交易編號。");
            }

            return false;
        }

        if (Status is PaymentStatus.InstructionsIssued
            or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            return false;
        }

        if (Status != PaymentStatus.Pending)
        {
            return Result<bool>.Failure(
                "payment.invalid-failure-transition",
                $"付款狀態 {Status} 不可標記為失敗。");
        }

        Status = PaymentStatus.Failed;
        ProviderTransactionId = providerTransactionId;
        return true;
    }

    public Result<bool> IssueInstructions(
        string providerTransactionId,
        PaymentInstructionData data,
        DateTimeOffset providerExpiresAt,
        DateTimeOffset issuedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerTransactionId);
        ArgumentNullException.ThrowIfNull(data);

        if (Status is PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            return false;
        }

        if (Status == PaymentStatus.InstructionsIssued)
        {
            return StringComparer.Ordinal.Equals(ProviderTransactionId, providerTransactionId)
                ? false
                : Result<bool>.Failure(
                    "payment.instructions-trade-no-mismatch",
                    "同一付款收到不同的取號交易編號。");
        }

        if (Status == PaymentStatus.Failed)
        {
            return Result<bool>.Failure(
                "payment.attempt-superseded",
                "這筆付款嘗試已失效，不能再寫入取號資訊。");
        }

        if (Status != PaymentStatus.Pending || Method is null || Method == PaymentMethod.CreditCard)
        {
            return Result<bool>.Failure(
                "payment.invalid-instructions-transition",
                "目前的付款狀態或付款方式不能寫入取號資訊。");
        }

        if (providerTransactionId.Length > 32 || providerExpiresAt < issuedAt ||
            !InstructionsMatchMethod(Method.Value, data))
        {
            return Result<bool>.Failure(
                "payment.invalid-payment-instructions",
                "綠界取號資訊缺漏、格式不符，或與付款方式不一致。");
        }

        Status = PaymentStatus.InstructionsIssued;
        ProviderTransactionId = providerTransactionId;
        BankCode = data.BankCode;
        VirtualAccount = data.VirtualAccount;
        PaymentNo = data.PaymentNo;
        Barcode1 = data.Barcode1;
        Barcode2 = data.Barcode2;
        Barcode3 = data.Barcode3;
        ProviderExpiresAt = providerExpiresAt;
        InstructionsIssuedAt = issuedAt;
        return true;
    }

    public Result<bool> ExpireInstructions(DateTimeOffset now)
    {
        if (Status == PaymentStatus.Failed)
        {
            return false;
        }

        if (Status != PaymentStatus.InstructionsIssued || ProviderExpiresAt is null)
        {
            return Result<bool>.Failure(
                "payment.invalid-instructions-expiry-transition",
                "目前的付款狀態不能將取號資訊標記為逾期。");
        }

        if (now < ProviderExpiresAt.Value.AddSeconds(1))
        {
            return Result<bool>.Failure(
                "payment.instructions-not-expired",
                "取號繳費期限尚未屆滿。");
        }

        Status = PaymentStatus.Failed;
        return true;
    }

    public bool Supersede(DateTimeOffset now)
    {
        if (Status != PaymentStatus.Pending)
        {
            return false;
        }

        Status = PaymentStatus.Failed;
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

    public PaymentSummary ToSummary()
    {
        PaymentInstructionsView? instructions = null;
        if (Method is { } method &&
            ProviderExpiresAt is { } providerExpiresAt &&
            InstructionsIssuedAt is { } instructionsIssuedAt)
        {
            var barcodes = new[] { Barcode1, Barcode2, Barcode3 }
                .Where(value => value is not null)
                .Cast<string>()
                .ToArray();
            instructions = new PaymentInstructionsView(
                method,
                BankCode,
                VirtualAccount,
                PaymentNo,
                barcodes.Length == 0 ? null : barcodes,
                providerExpiresAt,
                instructionsIssuedAt);
        }

        return new PaymentSummary(
            Id,
            OrderId,
            Provider,
            Status,
            Amount,
            Fee,
            ProviderTransactionId,
            CapturedAt,
            SettledAt,
            Method,
            instructions);
    }

    private static bool InstructionsMatchMethod(
        PaymentMethod method,
        PaymentInstructionData data)
    {
        static bool Present(string? value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= 32;

        return method switch
        {
            PaymentMethod.Atm =>
                Present(data.BankCode) &&
                Present(data.VirtualAccount) &&
                data.PaymentNo is null &&
                data.Barcode1 is null &&
                data.Barcode2 is null &&
                data.Barcode3 is null,
            PaymentMethod.ConvenienceStoreCode =>
                data.BankCode is null &&
                data.VirtualAccount is null &&
                Present(data.PaymentNo) &&
                data.Barcode1 is null &&
                data.Barcode2 is null &&
                data.Barcode3 is null,
            PaymentMethod.Barcode =>
                data.BankCode is null &&
                data.VirtualAccount is null &&
                data.PaymentNo is null &&
                Present(data.Barcode1) &&
                Present(data.Barcode2) &&
                Present(data.Barcode3),
            _ => false,
        };
    }
}

internal sealed record PaymentInstructionData(
    string? BankCode,
    string? VirtualAccount,
    string? PaymentNo,
    string? Barcode1,
    string? Barcode2,
    string? Barcode3);

internal enum CaptureTransition
{
    AlreadyCaptured = 0,
    Captured = 1,
    LateCaptureRecorded = 2,
    LateCaptureAlreadyRecorded = 3,
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
