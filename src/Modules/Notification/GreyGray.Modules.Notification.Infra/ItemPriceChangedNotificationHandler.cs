using System.Diagnostics;
using GreyGray.Modules.Notification.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Observability;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Notification.Infra;

/// <summary>
/// 現場漲價 → 發 LINE 問客人。<see cref="ItemPriceChanged"/> 本身不帶 CustomerId
/// （Procurement 不認識客人是誰），所以這裡用 CampaignId ＋ OrderLineId 反查訂單。
/// M0 只排入資料庫，不真正送 LINE——跟 <see cref="CustomerRegisteredNotificationHandler"/> 同一個限制。
/// </summary>
internal sealed class ItemPriceChangedNotificationHandler(
    NotificationDbContext dbContext,
    IOrderQuery orders,
    IClock clock,
    ICorrelationContext correlationContext)
    : IIntegrationEventHandler<ItemPriceChanged>
{
    public async Task HandleAsync(
        ItemPriceChanged @event,
        CancellationToken cancellationToken)
    {
        using var activity = GreyGrayTelemetry.ActivitySource.StartActivity(
            "notification queue item price changed",
            ActivityKind.Internal);

        var campaignOrders = await orders.GetByCampaignAsync(@event.CampaignId, cancellationToken);
        if (campaignOrders.IsFailure)
        {
            throw new InvalidOperationException(
                $"ItemPriceChanged 查不到對應開團的訂單：" +
                $"{campaignOrders.Error.Code} {campaignOrders.Error.Message}");
        }

        var owner = campaignOrders.Value.FirstOrDefault(
            order => order.Lines.Any(line => line.Id == @event.OrderLineId));
        if (owner is null)
        {
            throw new InvalidOperationException(
                $"ItemPriceChanged 的 OrderLineId {@event.OrderLineId} 在開團 " +
                $"{@event.CampaignId} 的訂單裡找不到對應品項。");
        }

        dbContext.Notifications.Add(new NotificationEntity(
            NotificationId.New(),
            @event.TenantId,
            owner.CustomerId.Value,
            NotificationChannel.Line,
            "procurement.item-price-changed",
            clock.UtcNow,
            Activity.Current?.TraceId.ToHexString() ?? correlationContext.CorrelationId,
            Activity.Current?.SpanId.ToHexString()));

        // SaveChanges 由 processed-message decorator 執行，讓 marker 與這筆副作用同交易。
    }
}
