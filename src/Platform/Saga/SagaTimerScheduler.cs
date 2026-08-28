using System.Text.Json;
using GreyGray.Platform.Abstractions.Saga;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Platform.Saga;

/// <summary>排程與取消持久化於 PostgreSQL 的 saga timer。</summary>
public class SagaTimerScheduler<TDbContext>(TDbContext dbContext, IClock clock)
    : ISagaTimerScheduler
    where TDbContext : DbContext
{
    public async Task<Guid> ScheduleAsync(
        string sagaType,
        string sagaId,
        DateTimeOffset fireAt,
        string payload,
        TenantId tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaId);
        ArgumentNullException.ThrowIfNull(payload);
        using var _ = JsonDocument.Parse(payload);

        var now = clock.UtcNow;
        var timerId = Guid.CreateVersion7(now);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO platform.saga_timer
                (id, tenant_id, saga_type, saga_id, fire_at, payload, created_at)
            VALUES
                ({timerId}, {tenantId.Value}, {sagaType}, {sagaId}, {fireAt}, {payload}::jsonb, {now});
            """, cancellationToken);
        return timerId;
    }

    public Task CancelAsync(Guid timerId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        return dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE platform.saga_timer
            SET cancelled_at = {now}
            WHERE id = {timerId}
              AND fired_at IS NULL
              AND cancelled_at IS NULL;
            """, cancellationToken);
    }

    public Task CancelAllForSagaAsync(
        string sagaType,
        string sagaId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaId);
        var now = clock.UtcNow;
        return dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE platform.saga_timer
            SET cancelled_at = {now}
            WHERE saga_type = {sagaType}
              AND saga_id = {sagaId}
              AND fired_at IS NULL
              AND cancelled_at IS NULL;
            """, cancellationToken);
    }
}

/// <summary>使用 <see cref="PlatformDbContext"/> 的 Platform Worker 排程器。</summary>
public sealed class SagaTimerScheduler(PlatformDbContext dbContext, IClock clock)
    : SagaTimerScheduler<PlatformDbContext>(dbContext, clock);
