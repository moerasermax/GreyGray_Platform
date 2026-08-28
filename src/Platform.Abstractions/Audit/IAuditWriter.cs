namespace GreyGray.Platform.Abstractions.Audit;

/// <summary>稽核分類。</summary>
public enum AuditCategory
{
    /// <summary>一般操作稽核（誰在什麼時候改了什麼）。</summary>
    Operation = 1,

    /// <summary>
    /// 個資存取紀錄。<b>每一次讀取客戶明文個資都要留一筆</b>，含存取理由。
    /// 這是個資法下最低限度的可問責性。
    /// </summary>
    PersonalDataAccess = 2,

    /// <summary>外部整合的完整請求／回應留存（金流、物流、發票）。</summary>
    ExternalIntegration = 3,

    /// <summary>權限變更。</summary>
    Authorization = 4,
}

/// <summary>
/// 主動寫入稽核。事件驅動的部分由 Audit 模組自己訂閱，
/// 這個介面只給「沒有對應事件、但必須留痕」的動作用——
/// 最主要就是 <c>ICustomerDirectory.GetContactAsync</c> 的個資讀取。
/// </summary>
/// <remarks>
/// <b>為什麼放在 Platform.Abstractions 而不是 Audit.Contracts</b>（ADR-017）：
/// Audit 是支撐模組，規則是「只訂閱事件，不被任何人依賴」。
/// 但個資存取留痕是<b>同步</b>的——事後補事件等於留下一段沒有稽核的空窗。
/// 若把 <see cref="IAuditWriter"/> 留在 <c>Audit.Contracts</c>，
/// <c>Identity.Core</c> 就必須參考支撐模組，那條邊界就破了。
/// 稽核寫入實際上是<b>橫切的平台能力</b>，不是模組能力，所以它屬於這裡。
/// <para>
/// 參數用 <see cref="Guid"/> 而不是 <c>StaffId</c>／<c>CustomerId</c>，
/// 是因為 <c>Platform.Abstractions</c> 只能參考 <c>Shared.Kernel</c>
/// （由 <c>Architecture.Tests</c> 斷言）。稽核本來就是泛型的接收端，
/// <paramref name="targetType"/>／<paramref name="targetRef"/> 也早就是字串。
/// </para>
/// </remarks>
public interface IAuditWriter
{
    /// <param name="category">稽核分類。</param>
    /// <param name="action">動作代碼，例如 <c>customer.contact.read</c>。</param>
    /// <param name="targetType">被操作對象的型別名，例如 <c>Customer</c>。</param>
    /// <param name="targetRef">被操作對象的識別。</param>
    /// <param name="actorId">操作者（團隊成員）。系統自動觸發時為 null。</param>
    /// <param name="subjectId">個資主體（客戶）。與客戶無關的操作為 null。</param>
    /// <param name="reason">存取理由。<see cref="AuditCategory.PersonalDataAccess"/> 時必填。</param>
    /// <param name="payloadJson">補充內容，jsonb。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    Task WriteAsync(
        AuditCategory category,
        string action,
        string targetType,
        string targetRef,
        Guid? actorId,
        Guid? subjectId,
        string? reason,
        string payloadJson,
        CancellationToken cancellationToken);
}
