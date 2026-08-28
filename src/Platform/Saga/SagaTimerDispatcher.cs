using System.Data;
using GreyGray.Platform.Observability;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GreyGray.Platform.Saga;

/// <summary>持有固定 PostgreSQL 掃描鎖，派送已到期的 saga timer。</summary>
public sealed class SagaTimerDispatcher
{
    public const long AdvisoryLockKey = 1002;

    private readonly PlatformDbContext _dbContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<string, SagaTimeoutHandlerRegistration> _handlers;
    private readonly CorrelationContext _correlationContext;
    private readonly IClock _clock;
    private readonly ILogger<SagaTimerDispatcher> _logger;

    public SagaTimerDispatcher(
        PlatformDbContext dbContext,
        IServiceProvider serviceProvider,
        IEnumerable<SagaTimeoutHandlerRegistration> handlers,
        CorrelationContext correlationContext,
        IClock clock,
        ILogger<SagaTimerDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(correlationContext);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _dbContext = dbContext;
        _serviceProvider = serviceProvider;
        _correlationContext = correlationContext;
        _clock = clock;
        _logger = logger;
        var registrations = handlers.ToArray();
        var duplicateSagaType = registrations
            .GroupBy(handler => handler.SagaType, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Skip(1).Any())
            ?.Key;
        if (duplicateSagaType is not null)
        {
            throw new InvalidOperationException(
                $"saga type '{duplicateSagaType}' 重複登錄了 timeout handler。");
        }

        _handlers = registrations.ToDictionary(
            handler => handler.SagaType,
            StringComparer.Ordinal);
    }

    public async Task<int> DispatchDueAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "Saga timer dispatcher 必須自行管理資料庫交易。");
        }

        var connection = _dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var ownsLock = false;
        try
        {
            await using (var lockCommand = connection.CreateCommand())
            {
                lockCommand.CommandText = "SELECT pg_try_advisory_lock(1002);";
                ownsLock = (bool)(await lockCommand.ExecuteScalarAsync(cancellationToken)
                    ?? false);
            }

            if (!ownsLock)
            {
                return 0;
            }

            var now = _clock.UtcNow;
            var timerIds = await _dbContext.Set<SagaTimer>()
                .AsNoTracking()
                .Where(timer => timer.FiredAt == null
                    && timer.CancelledAt == null
                    && timer.FireAt <= now)
                .OrderBy(timer => timer.FireAt)
                .ThenBy(timer => timer.Id)
                .Select(timer => timer.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            var dispatched = 0;
            foreach (var timerId in timerIds)
            {
                await using var transaction = await _dbContext.Database
                    .BeginTransactionAsync(cancellationToken);
                try
                {
                    var timer = await _dbContext.Set<SagaTimer>()
                        .FromSqlInterpolated($"""
                            SELECT id, tenant_id, saga_type, saga_id, fire_at, payload,
                                   fired_at, cancelled_at, created_at
                            FROM platform.saga_timer
                            WHERE id = {timerId}
                              AND fired_at IS NULL
                              AND cancelled_at IS NULL
                            FOR UPDATE
                            """)
                        .SingleOrDefaultAsync(cancellationToken);

                    if (timer is null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        continue;
                    }

                    if (!_handlers.TryGetValue(timer.SagaType, out var registration))
                    {
                        throw new InvalidOperationException(
                            $"saga type '{timer.SagaType}' 尚未登錄 timeout handler。");
                    }

                    var correlationId = _correlationContext.CorrelationId;
                    var causationId = _correlationContext.CausationId;
                    using var correlationScope = _correlationContext.BeginScope(
                        correlationId,
                        causationId,
                        timer.TenantId);

                    var tenantId = timer.TenantId.Value.ToString("D");
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                        SELECT set_config('app.tenant_id', {tenantId}, true);
                        """, cancellationToken);

                    await using var scope = _serviceProvider.CreateAsyncScope();
                    await registration.InvokeAsync(
                        scope.ServiceProvider,
                        timer.SagaId,
                        timer.Payload,
                        cancellationToken);

                    timer.FiredAt = _clock.UtcNow;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    dispatched++;
                }
                catch (Exception exception)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    _dbContext.ChangeTracker.Clear();
                    _logger.LogError(
                        exception,
                        "Saga timer {TimerId} 派送失敗，將保持待處理狀態以供重試。",
                        timerId);
                    throw;
                }
            }

            return dispatched;
        }
        finally
        {
            if (ownsLock)
            {
                try
                {
                    await using var unlockCommand = connection.CreateCommand();
                    unlockCommand.CommandText = "SELECT pg_advisory_unlock(1002);";
                    await unlockCommand.ExecuteScalarAsync(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "無法明確釋放 saga timer advisory lock {LockKey}。",
                        AdvisoryLockKey);
                    if (!closeConnection)
                    {
                        throw;
                    }
                }
            }

            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
