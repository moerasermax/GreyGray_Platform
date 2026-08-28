using GreyGray.Modules.Notification.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Notification.Infra;

/// <summary>M0 只排入資料庫，不真正送 LINE。</summary>
internal sealed class NotificationEntity
{
    private NotificationEntity()
    {
    }

    public NotificationEntity(
        NotificationId id,
        TenantId tenantId,
        Guid customerId,
        NotificationChannel channel,
        string templateCode,
        DateTimeOffset createdAt,
        string traceId,
        string? handlerSpanId)
    {
        Id = id;
        TenantId = tenantId;
        CustomerId = customerId;
        Channel = channel;
        TemplateCode = templateCode;
        Status = NotificationStatus.Queued;
        CreatedAt = createdAt;
        TraceId = traceId;
        HandlerSpanId = handlerSpanId;
    }

    public NotificationId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public Guid CustomerId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public string TemplateCode { get; private set; } = string.Empty;

    public NotificationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string TraceId { get; private set; } = string.Empty;

    public string? HandlerSpanId { get; private set; }
}
