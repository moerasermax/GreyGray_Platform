using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Observability;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GreyGray.Platform.Outbox;

/// <summary>
/// 以 PostgreSQL row lock 安全地平行派送 outbox。派送語意是 at-least-once；
/// handler 的去重由 processed_message 包裝負責。
/// </summary>
public sealed class OutboxDispatcher(
    PlatformDbContext dbContext,
    IServiceProvider serviceProvider,
    EventTypeRegistry eventTypeRegistry,
    CorrelationContext correlationContext,
    IClock clock,
    ILogger<OutboxDispatcher> logger) : IOutboxDispatcher
{
    private const int MaxDeliveryAttempts = 10;
    private const int MaxBackoffSeconds = 60 * 60;

    public async Task<int> DispatchBatchAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        var messages = await dbContext.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT *
                FROM platform.outbox_message
                WHERE processed_at IS NULL
                  AND dead_lettered = false
                  AND next_attempt_at <= now()
                ORDER BY next_attempt_at, occurred_at, id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var processedCount = 0;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await SetTenantAsync(message.TenantId, cancellationToken);

                using var scope = correlationContext.BeginScope(
                    message.CorrelationId,
                    message.CausationId,
                    message.TenantId);
                using var activity = GreyGrayTelemetry.StartConsumerActivity(
                    message.EventType,
                    message.CorrelationId,
                    message.CausationId);

                try
                {
                    if (!eventTypeRegistry.TryResolve(message.EventType, out var eventClrType))
                    {
                        MarkDeadLetter(
                            message,
                            $"未登錄的整合事件型別：'{message.EventType}'。");
                        continue;
                    }

                    var @event = JsonSerializer.Deserialize(
                        message.Payload,
                        eventTypeRegistry.JsonTypeInfoOf(eventClrType))
                        ?? throw new JsonException(
                            $"事件 '{message.EventType}' 的 payload 反序列化結果是 null。");

                    await InvokeHandlersAsync(eventClrType, @event, cancellationToken);

                    message.ProcessedAt = clock.UtcNow;
                    message.LastError = null;
                    processedCount++;
                }
                catch (Exception exception)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                    throw;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                MarkDeliveryFailure(message, exception);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return processedCount;
    }

    private async Task SetTenantAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        // SET LOCAL 文法不接受 bind parameter；set_config(..., true) 與它等價，
        // 且能讓 tenant id 保持參數化。第三個參數 true 確保 transaction pooling
        // 不會把租戶設定洩漏到下一個借用同一條連線的請求。
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.tenant_id', {tenantId.Value.ToString("D")}, true)
            """, cancellationToken);
    }

    private async Task InvokeHandlersAsync(
        Type eventClrType,
        object @event,
        CancellationToken cancellationToken)
    {
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventClrType);
        var handleMethod = handlerType.GetMethod("HandleAsync")
            ?? throw new InvalidOperationException($"找不到 {handlerType.FullName}.HandleAsync。");

        foreach (var resolvedHandler in serviceProvider.GetServices(handlerType))
        {
            var handler = resolvedHandler
                ?? throw new InvalidOperationException($"DI 為 {handlerType.FullName} 回傳 null。");
            Task task;
            try
            {
                task = (Task?)handleMethod.Invoke(handler, [@event, cancellationToken])
                    ?? throw new InvalidOperationException(
                        $"{handler.GetType().FullName}.HandleAsync 回傳 null。");
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }

            await task;
        }
    }

    private void MarkDeliveryFailure(OutboxMessage message, Exception exception)
    {
        message.Attempts++;
        message.LastError = exception.ToString();
        message.NextAttemptAt = clock.UtcNow.Add(BackoffFor(message.Attempts));

        if (message.Attempts >= MaxDeliveryAttempts)
        {
            message.IsDeadLettered = true;
            logger.LogCritical(
                exception,
                "Outbox 訊息 {MessageId}（{EventType}）已失敗 {Attempts} 次並進入死信。",
                message.Id,
                message.EventType,
                message.Attempts);
            return;
        }

        logger.LogWarning(
            exception,
            "Outbox 訊息 {MessageId}（{EventType}）第 {Attempts} 次派送失敗，下次嘗試時間 {NextAttemptAt}。",
            message.Id,
            message.EventType,
            message.Attempts,
            message.NextAttemptAt);
    }

    private void MarkDeadLetter(OutboxMessage message, string error)
    {
        message.Attempts++;
        message.LastError = error;
        message.NextAttemptAt = clock.UtcNow;
        message.IsDeadLettered = true;

        logger.LogCritical(
            "Outbox 訊息 {MessageId} 帶有未知 EventType {EventType}，已直接進入死信。",
            message.Id,
            message.EventType);
    }

    private static TimeSpan BackoffFor(int attempts)
    {
        var exponent = Math.Min(attempts, 12);
        var seconds = Math.Min(1 << exponent, MaxBackoffSeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
