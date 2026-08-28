using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Audit.Contracts;

// ── 支撐模組：只訂閱事件，不被任何人依賴 ───────────────────────────────

public readonly record struct AuditRecordId(Guid Value)
{
    public static AuditRecordId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

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

public sealed record AuditRecord(
    AuditRecordId Id,
    AuditCategory Category,
    string Action,
    string TargetType,
    string TargetRef,
    StaffId? Actor,
    CustomerId? Subject,
    string? Reason,
    string PayloadJson,
    string CorrelationId,
    DateTimeOffset OccurredAt);

/// <summary>
/// 稽核查詢。<b>只有讀取，沒有刪除也沒有更新</b>——不可竄改追溯是這個模組存在的理由。
/// audit schema 的 DB role 只有 INSERT 與 SELECT，沒有 UPDATE／DELETE。
/// </summary>
public interface IAuditQuery
{
    Task<Result<IReadOnlyList<AuditRecord>>> SearchAsync(
        AuditCategory? category,
        string? targetType,
        string? targetRef,
        DateTimeOffset from,
        DateTimeOffset to,
        string? cursor,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// 主動寫入稽核。事件驅動的部分由 Audit 自己訂閱，
/// 這個介面只給「沒有對應事件、但必須留痕」的動作用——
/// 最主要就是 <see cref="ICustomerDirectory.GetContactAsync"/> 的個資讀取。
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(
        AuditCategory category,
        string action,
        string targetType,
        string targetRef,
        StaffId? actor,
        CustomerId? subject,
        string? reason,
        string payloadJson,
        CancellationToken cancellationToken);
}

public sealed record AuditRecorded(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    AuditRecordId RecordId,
    AuditCategory Category,
    string Action)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "audit.AuditRecorded.v1";
}
