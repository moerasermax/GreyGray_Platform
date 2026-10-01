namespace GreyGray.Modules.Ordering.Core;

/// <summary>訂單資料在持久化時發生樂觀併發衝突。</summary>
public sealed class OrderingConcurrencyException(string message, Exception innerException)
    : Exception(message, innerException);
