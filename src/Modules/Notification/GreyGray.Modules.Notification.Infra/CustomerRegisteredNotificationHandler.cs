using System.Diagnostics;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Notification.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Observability;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Notification.Infra;

internal sealed class CustomerRegisteredNotificationHandler(
    NotificationDbContext dbContext,
    IClock clock,
    ICorrelationContext correlationContext)
    : IIntegrationEventHandler<CustomerRegistered>
{
    public Task HandleAsync(
        CustomerRegistered @event,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = GreyGrayTelemetry.ActivitySource.StartActivity(
            "notification queue customer registered",
            ActivityKind.Internal);

        dbContext.Notifications.Add(new NotificationEntity(
            NotificationId.New(),
            @event.TenantId,
            @event.CustomerId.Value,
            NotificationChannel.Line,
            "customer.registered",
            clock.UtcNow,
            Activity.Current?.TraceId.ToHexString() ?? correlationContext.CorrelationId,
            Activity.Current?.SpanId.ToHexString()));

        // SaveChanges 由 processed-message decorator 執行，讓 marker 與這筆副作用同交易。
        return Task.CompletedTask;
    }
}
