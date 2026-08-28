using Daigou.Modules.Identity.Contracts;
using Daigou.Platform.Abstractions.Messaging;
using Daigou.Shared.Kernel;

namespace Daigou.Modules.Notification.Contracts;

// ── 支撐模組：只訂閱事件，不被任何人依賴 ───────────────────────────────
//
// 快速檢查邊界切得對不對：如果 Ordering 需要 import Notification 才能發通知，
// 邊界就已經破了。Notification 訂閱 ItemPriceChanged 等事件後自己決定要發什麼。
//
// 客人對漲價詢問的回覆，走 Procurement 的 IInquiryReplyReceiver（ADR-012），
// 不從這裡發事件回去——否則 Procurement 就會反過來依賴支撐模組。

public readonly record struct NotificationId(Guid Value)
{
    public static NotificationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public enum NotificationChannel
{
    /// <summary>
    /// LINE 是主力。<b>現場問客人「這項漲價了還要嗎」是業務關鍵路徑</b>，
    /// 不只是通知——所以這個模組不能被當成可有可無的旁支。
    /// </summary>
    Line = 1,

    Email = 2,

    /// <summary>M6+。</summary>
    Sms = 3,
}

public enum NotificationStatus
{
    Queued = 0,
    Sent = 1,
    Delivered = 2,
    Failed = 3,
}

public sealed record NotificationRecord(
    NotificationId Id,
    CustomerId? CustomerId,
    NotificationChannel Channel,
    string TemplateCode,
    NotificationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    string? FailureReason);

public interface INotificationQuery
{
    Task<Result<IReadOnlyList<NotificationRecord>>> GetByCustomerAsync(
        CustomerId customerId,
        int limit,
        CancellationToken cancellationToken);
}

// ── 對外事件（只給 Audit / Reporting 用，業務模組不訂閱）───────────────

public sealed record NotificationSent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    NotificationId NotificationId,
    CustomerId? CustomerId,
    NotificationChannel Channel,
    string TemplateCode)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "notify.NotificationSent.v1";
}

public sealed record NotificationFailed(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    NotificationId NotificationId,
    NotificationChannel Channel,
    string TemplateCode,
    string Reason)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "notify.NotificationFailed.v1";
}
