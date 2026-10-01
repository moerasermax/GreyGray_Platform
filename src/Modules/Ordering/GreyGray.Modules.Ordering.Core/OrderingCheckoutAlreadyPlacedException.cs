namespace GreyGray.Modules.Ordering.Core;

/// <summary>結帳建單時，同一購物車或結帳事件已由另一個執行者先建立訂單。</summary>
public sealed class OrderingCheckoutAlreadyPlacedException(string message, Exception innerException)
    : Exception(message, innerException);
