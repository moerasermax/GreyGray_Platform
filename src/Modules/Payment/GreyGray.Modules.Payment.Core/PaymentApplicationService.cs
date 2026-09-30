using System.Globalization;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Payment.Core;

internal sealed record EcpaySettings(
    string MerchantId,
    Uri CheckoutUrl,
    Uri CreditDetailUrl,
    TimeSpan InitiationLifetime,
    TimeSpan CallbackMaxAge,
    bool AllowSimulatedPaid);

/// <summary>
/// 綠界信用卡退刷（<c>/CreditDetail/DoAction</c>，<c>Action=R</c>）的回應。
/// <see cref="RawResponse"/> 保留原始回應內容，供失敗時留痕查證（客服會問）。
/// </summary>
internal sealed record EcpayRefundResult(bool Succeeded, string RtnCode, string RtnMsg, string RawResponse);

internal sealed record VerifiedEcpayCallback(
    string MerchantTradeNo,
    string TradeNo,
    int RtnCode,
    string RtnCodeText,
    long TradeAmountMajor);

internal interface IEcpayGateway
{
    IReadOnlyDictionary<string, string> CreateCheckoutFields(
        string merchantTradeNo,
        Money amount,
        string description,
        Uri returnUrl,
        Uri clientBackUrl,
        DateTimeOffset createdAt);

    bool VerifyCallback(IReadOnlyDictionary<string, string> fields);

    /// <summary>
    /// 對已請款（關帳）的信用卡交易發動原路退刷。<b>綠界測試環境無法提供真實授權，
    /// 因此這個 API 官方文件明講測試環境不可用</b>——呼叫端不要假設 stage 一定會退款成功。
    /// </summary>
    Task<EcpayRefundResult> RequestRefundAsync(
        string merchantTradeNo,
        string providerTransactionId,
        Money amount,
        CancellationToken cancellationToken);
}

internal sealed class PaymentApplicationService(
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    IEventPublisher eventPublisher,
    IEcpayGateway ecpay,
    EcpaySettings settings,
    IClock clock,
    ICorrelationContext correlationContext) : IPaymentCommand, IPaymentQuery, IEcpayCallbackVerifier
{
    // 為什麼不直接 FindSystemTimeZoneById("Asia/Taipei")：見 TaipeiTime 的註解——
    // InvariantGlobalization 關掉 ICU 之後，Windows 上查不到 IANA 那個名字。
    private static readonly TimeZoneInfo TaipeiTimeZone = TaipeiTime.Zone;

    public async Task<Result<PaymentInitiation>> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.GoodsAmount.Currency != Currency.TWD ||
            request.ShippingAmount.Currency != Currency.TWD)
        {
            return Result<PaymentInitiation>.Failure(
                "payment.unsupported-currency",
                "綠界付款目前只支援新台幣。");
        }

        var total = request.GoodsAmount.Add(request.ShippingAmount);
        if (total.AmountMinor <= 0 || total.AmountMinor % Currency.TWD.MinorUnitsPerUnit() != 0)
        {
            return Result<PaymentInitiation>.Failure(
                "payment.invalid-amount",
                "綠界付款金額必須是大於零的新台幣整數元。");
        }

        var existing = await payments.FindByOrderAsync(
            correlationContext.TenantId,
            request.OrderId,
            cancellationToken);
        if (existing?.Status is PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            return Result<PaymentInitiation>.Failure(
                "ordering.order-already-paid",
                "這張訂單已付款。");
        }

        var now = clock.UtcNow;
        Payment payment;
        if (existing is { Status: PaymentStatus.Pending } && existing.ExpiresAt > now)
        {
            payment = existing;
            if (payment.Amount != total)
            {
                return Result<PaymentInitiation>.Failure(
                    "payment.amount-mismatch",
                    "付款要求金額與訂單原應付金額不符。");
            }

            if (payment.SetBreakdown(request.GoodsAmount, request.ShippingAmount))
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            if (existing is { Status: PaymentStatus.Pending })
            {
                existing.Expire(now);
                // active-payment partial unique 是 immediate；先封存舊 attempt，避免 EF
                // 若先送 INSERT 再送 UPDATE 時誤撞。第二段失敗仍可由下一次請求重試。
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var paymentId = PaymentId.New();
            var merchantTradeNo = $"GG{paymentId.Value:N}"[..20];
            payment = Payment.Start(
                paymentId,
                correlationContext.TenantId,
                request.OrderId,
                request.GoodsAmount,
                request.ShippingAmount,
                merchantTradeNo,
                now,
                now.Add(settings.InitiationLifetime));
            payments.Add(payment);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var fields = ecpay.CreateCheckoutFields(
            payment.MerchantTradeNo,
            payment.Amount,
            request.Description,
            request.ReturnUrl,
            request.ClientBackUrl,
            payment.CreatedAt);

        return new PaymentInitiation(
            PaymentProvider.ECPay,
            "POST",
            settings.CheckoutUrl,
            fields,
            payment.ExpiresAt);
    }

    public async Task<Result> HandleEcpayCallbackAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        var verification = VerifyCallback(fields);
        if (verification.IsFailure)
        {
            return Result.Failure(verification.Error);
        }

        var callback = verification.Value;

        var payment = await payments.FindByMerchantTradeNoAsync(
            correlationContext.TenantId,
            callback.MerchantTradeNo,
            cancellationToken);
        if (payment is null)
        {
            return Result.Failure("payment.payment-not-found", "找不到綠界回呼對應的付款。");
        }

        if ((payment.Status is PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Refunded) &&
            StringComparer.Ordinal.Equals(payment.ProviderTransactionId, callback.TradeNo))
        {
            return Result.Success();
        }

        if (fields.TryGetValue("SimulatePaid", out var simulated) &&
            simulated == "1" &&
            !settings.AllowSimulatedPaid)
        {
            return Result.Failure("payment.simulated-callback-rejected", "正式流程不可接受模擬付款通知。");
        }

        var expectedMajor = payment.Amount.AmountMinor / Currency.TWD.MinorUnitsPerUnit();
        if (callback.TradeAmountMajor != expectedMajor)
        {
            return Result.Failure("payment.amount-mismatch", "綠界回呼金額與訂單應付金額不符。");
        }

        if (!TryGetCallbackTime(fields, out var callbackTime) ||
            (clock.UtcNow - callbackTime).Duration() > settings.CallbackMaxAge)
        {
            return Result.Failure("payment.stale-callback", "綠界回呼時間超出容忍範圍。");
        }

        var occurredAt = clock.UtcNow;
        if (callback.RtnCode == 1)
        {
            var fee = ParseFee(fields, payment.Amount.Currency);
            if (payment.Capture(callback.TradeNo, fee, callbackTime))
            {
                await eventPublisher.PublishAsync(
                    new PaymentCaptured(
                        Guid.CreateVersion7(),
                        occurredAt,
                        payment.TenantId,
                        payment.Id,
                        payment.OrderId,
                        payment.Provider,
                        payment.Amount,
                        payment.GoodsAmount,
                        payment.ShippingAmount,
                        callback.TradeNo),
                    cancellationToken);
            }
        }
        else if (payment.Fail(callback.TradeNo))
        {
            fields.TryGetValue("RtnMsg", out var message);
            await eventPublisher.PublishAsync(
                new PaymentFailed(
                    Guid.CreateVersion7(),
                    occurredAt,
                    payment.TenantId,
                    payment.Id,
                    payment.OrderId,
                    payment.Provider,
                    callback.RtnCodeText,
                    message ?? "綠界回報付款失敗。"),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public Result<EcpayCallbackEnvelope> Verify(IReadOnlyDictionary<string, string> fields)
    {
        var verification = VerifyCallback(fields);
        return verification.IsFailure
            ? Result<EcpayCallbackEnvelope>.Failure(verification.Error)
            : new EcpayCallbackEnvelope(
                verification.Value.MerchantTradeNo,
                verification.Value.TradeNo,
                verification.Value.RtnCode);
    }

    public async Task<Result<PaymentSummary>> GetAsync(
        PaymentId id,
        CancellationToken cancellationToken)
    {
        var payment = await payments.FindByIdAsync(
            correlationContext.TenantId,
            id,
            cancellationToken);
        return payment is null
            ? Result<PaymentSummary>.Failure("payment.not-found", "找不到付款紀錄。")
            : payment.ToSummary();
    }

    public async Task<Result<IReadOnlyList<PaymentSummary>>> GetByOrderAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        var found = await payments.FindByOrderAllAsync(
            correlationContext.TenantId,
            orderId,
            cancellationToken);
        return found.Select(payment => payment.ToSummary()).ToArray();
    }

    public Task<Result<IReadOnlyList<ProviderCapability>>> GetEnabledProvidersAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderCapability> providers =
        [new(PaymentProvider.ECPay, true, true, true)];
        return Task.FromResult(Result<IReadOnlyList<ProviderCapability>>.Success(providers));
    }

    private static bool Required(
        IReadOnlyDictionary<string, string> fields,
        string key,
        out string value)
    {
        if (fields.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private Result<VerifiedEcpayCallback> VerifyCallback(
        IReadOnlyDictionary<string, string> fields)
    {
        if (!ecpay.VerifyCallback(fields))
        {
            return Result<VerifiedEcpayCallback>.Failure(
                "payment.invalid-signature",
                "綠界回呼驗簽失敗。");
        }

        if (!Required(fields, "MerchantID", out var merchantId) ||
            !StringComparer.Ordinal.Equals(merchantId, settings.MerchantId) ||
            !Required(fields, "MerchantTradeNo", out var merchantTradeNo) ||
            !Required(fields, "TradeNo", out var tradeNo) ||
            !Required(fields, "RtnCode", out var rtnCodeText) ||
            !int.TryParse(rtnCodeText, NumberStyles.None, CultureInfo.InvariantCulture, out var rtnCode) ||
            !Required(fields, "TradeAmt", out var tradeAmountText) ||
            !long.TryParse(tradeAmountText, NumberStyles.None, CultureInfo.InvariantCulture, out var tradeAmountMajor))
        {
            return Result<VerifiedEcpayCallback>.Failure(
                "payment.invalid-callback",
                "綠界回呼缺少必要欄位或格式錯誤。");
        }

        return new VerifiedEcpayCallback(
            merchantTradeNo,
            tradeNo,
            rtnCode,
            rtnCodeText,
            tradeAmountMajor);
    }

    private static bool TryGetCallbackTime(
        IReadOnlyDictionary<string, string> fields,
        out DateTimeOffset callbackTime)
    {
        var raw = fields.TryGetValue("PaymentDate", out var paymentDate) &&
                  !string.IsNullOrWhiteSpace(paymentDate)
            ? paymentDate
            : fields.GetValueOrDefault("TradeDate");

        if (!DateTime.TryParseExact(
                raw,
                "yyyy/MM/dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var local))
        {
            callbackTime = default;
            return false;
        }

        // ★ 一定要正規化成 UTC 偏移才回去。
        //
        // 綠界的 PaymentDate／TradeDate 是「台北的牆上時間」，配上 +08:00 才是正確的瞬間；
        // 但這個值會一路傳到 Payment.Capture(...) 的 CapturedAt，最後落進 timestamptz 欄位，
        // 而 Npgsql 只接受偏移為 0 的 DateTimeOffset：
        //     System.ArgumentException: Cannot write DateTimeOffset with Offset=08:00:00 to
        //     PostgreSQL type 'timestamp with time zone', only offset 0 (UTC) is supported.
        // BE-40 第二輪由 Leader 用真環境走完整流程時撞到（回呼端點 500，付款留在 Pending）——
        // 在此之前所有測試都用 in-memory 樁，沒有一條把回呼寫進真的 DB。
        //
        // ToUniversalTime() 不改變「哪一個瞬間」，只換表示法，所以上面 CallbackMaxAge 的
        // Duration 比較、以及送進事件的時間語意都完全不變。
        callbackTime = new DateTimeOffset(
            local,
            TaipeiTimeZone.GetUtcOffset(local)).ToUniversalTime();
        return true;
    }

    private static Money ParseFee(
        IReadOnlyDictionary<string, string> fields,
        Currency currency)
    {
        if (!fields.TryGetValue("PaymentTypeChargeFee", out var raw))
        {
            return Money.Zero(currency);
        }

        var parts = raw.Trim().Split('.', 2);
        if (parts.Length == 0 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            major < 0)
        {
            return Money.Zero(currency);
        }

        var fraction = parts.Length == 2 ? parts[1] : string.Empty;
        if (fraction.Length > 2 || fraction.Any(character => !char.IsAsciiDigit(character)))
        {
            return Money.Zero(currency);
        }

        var minorText = fraction.PadRight(2, '0');
        var minorFraction = minorText.Length == 0
            ? 0L
            : long.Parse(minorText, NumberStyles.None, CultureInfo.InvariantCulture);
        return new Money(checked(major * currency.MinorUnitsPerUnit() + minorFraction), currency);
    }
}
