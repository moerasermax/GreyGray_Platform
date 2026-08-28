using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Audit;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Audit.Contracts;

// ── 支撐模組：只訂閱事件，不被任何人依賴 ───────────────────────────────
//
// AuditCategory 與 IAuditWriter 不在這裡，在 GreyGray.Platform.Abstractions.Audit（ADR-017）。
// 理由：個資存取留痕是「同步」的——Identity 讀客戶明文時就要寫一筆，不能事後補事件。
// 若把 IAuditWriter 留在這個組件裡，Identity.Core 就得參考支撐模組，那條邊界就破了。
// 稽核寫入實際上是橫切的平台能力，不是模組能力。

public readonly record struct AuditRecordId(Guid Value)
{
    public static AuditRecordId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
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

    public override string AggregateType => "AuditRecord";

    public override string AggregateId => RecordId.ToString();
}
