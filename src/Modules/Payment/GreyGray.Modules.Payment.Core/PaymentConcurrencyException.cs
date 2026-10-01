namespace GreyGray.Modules.Payment.Core;

/// <summary>
/// 付款資料在持久化時發生樂觀併發衝突。
/// </summary>
public sealed class PaymentConcurrencyException(string message, Exception innerException)
    : Exception(message, innerException);
