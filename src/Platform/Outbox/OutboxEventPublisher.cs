using System.Text.Json;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Observability;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Platform.Outbox;

/// <summary>
/// 把整合事件加入呼叫端 DbContext 的 outbox；不儲存、不派送。
/// 呼叫端後續的一次 <c>SaveChanges</c> 會同時寫入業務資料與 outbox。
/// </summary>
public sealed class OutboxEventPublisher<TDbContext>(
    TDbContext dbContext,
    ICorrelationContext correlationContext,
    EventTypeRegistry eventTypeRegistry) : IEventPublisher
    where TDbContext : DbContext
{
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = GreyGrayTelemetry.StartProducerActivity(TEvent.EventType);

        var eventClrType = typeof(TEvent);
        var registeredEventType = eventTypeRegistry.EventTypeOf(eventClrType);
        if (!StringComparer.Ordinal.Equals(registeredEventType, TEvent.EventType))
        {
            throw new InvalidOperationException(
                $"事件型別登錄不一致：{eventClrType.FullName} 的靜態 EventType 是 " +
                $"'{TEvent.EventType}'，登錄值卻是 '{registeredEventType}'。");
        }

        var correlationId = correlationContext.CorrelationId;
        var causationId = correlationContext.CausationId;
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var payload = JsonSerializer.Serialize(
            @event,
            eventTypeRegistry.JsonTypeInfoOf(eventClrType));

        dbContext.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = @event.EventId,
            TenantId = @event.TenantId,
            AggregateType = @event.AggregateType,
            AggregateId = @event.AggregateId,
            EventType = registeredEventType,
            Payload = payload,
            OccurredAt = @event.OccurredAt,
            CorrelationId = correlationId,
            CausationId = causationId,
            Attempts = 0,
            NextAttemptAt = @event.OccurredAt,
            IsDeadLettered = false,
        });

        return Task.CompletedTask;
    }
}
