using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Abstractions.Saga;
using GreyGray.Platform.Idempotency;
using GreyGray.Platform.Observability;
using GreyGray.Platform.Saga;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.Platform.Tests;

public sealed class IdempotencyAndSagaTests : IAsyncLifetime
{
    private const string DatabaseSchema = """
        DROP SCHEMA IF EXISTS platform CASCADE;
        CREATE SCHEMA platform;

        CREATE TABLE platform.idempotency_key (
            key               text         NOT NULL,
            scope             text         NOT NULL,
            request_hash      text         NOT NULL,
            status            text         NOT NULL,
            response_snapshot jsonb,
            created_at        timestamptz  NOT NULL DEFAULT now(),
            expires_at        timestamptz  NOT NULL,
            PRIMARY KEY (key, scope),
            CONSTRAINT idempotency_status_check
                CHECK (status IN ('IN_FLIGHT', 'COMPLETED', 'ABANDONED'))
        );
        CREATE INDEX ix_idempotency_expiry
            ON platform.idempotency_key (expires_at);

        CREATE TABLE platform.saga_timer (
            id           uuid        PRIMARY KEY,
            tenant_id    uuid        NOT NULL,
            saga_type    text        NOT NULL,
            saga_id      text        NOT NULL,
            fire_at      timestamptz NOT NULL,
            payload      jsonb       NOT NULL,
            fired_at     timestamptz,
            cancelled_at timestamptz,
            created_at   timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX ix_saga_timer_pending
            ON platform.saga_timer (fire_at)
            WHERE fired_at IS NULL AND cancelled_at IS NULL;
        CREATE INDEX ix_saga_timer_saga
            ON platform.saga_timer (saga_type, saga_id)
            WHERE fired_at IS NULL AND cancelled_at IS NULL;
        """;

    private static readonly DateTimeOffset InitialTime =
        new(2026, 8, 28, 4, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();
    private readonly AdjustableClock _clock = new(InitialTime);
    private readonly CorrelationContext _correlationContext = new();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Completed_request_returns_exact_first_snapshot_including_original_status_code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);
        const string snapshot = "{\"statusCode\":201,\"body\":{\"orderId\":\"first\"}}";
        var businessExecutions = 0;

        await using (var firstContext = CreateContext())
        {
            var firstStore = new IdempotencyStore(firstContext, _clock);
            var first = await firstStore.TryBeginAsync(
                "checkout-1",
                "checkout.submit",
                "hash-a",
                cancellationToken);
            first.Outcome.ShouldBe(IdempotencyOutcome.Proceed);
            businessExecutions++;
            await firstStore.CompleteAsync(
                "checkout-1",
                "checkout.submit",
                snapshot,
                cancellationToken);
        }

        await using var replayContext = CreateContext();
        var replayStore = new IdempotencyStore(replayContext, _clock);
        var replay = await replayStore.TryBeginAsync(
            "checkout-1",
            "checkout.submit",
            "hash-a",
            cancellationToken);

        replay.Outcome.ShouldBe(IdempotencyOutcome.AlreadyCompleted);
        replay.cachedResponse.ShouldBe(snapshot);
        businessExecutions.ShouldBe(1);
    }

    [Fact]
    public async Task Reusing_key_with_different_payload_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);

        await using var ownerContext = CreateContext();
        var owner = new IdempotencyStore(ownerContext, _clock);
        (await owner.TryBeginAsync("same-key", "orders", "hash-a", cancellationToken))
            .Outcome.ShouldBe(IdempotencyOutcome.Proceed);

        await using var otherContext = CreateContext();
        var other = new IdempotencyStore(otherContext, _clock);
        var result = await other.TryBeginAsync(
            "same-key",
            "orders",
            "hash-b",
            cancellationToken);

        result.Outcome.ShouldBe(IdempotencyOutcome.KeyReusedWithDifferentPayload);
    }

    [Fact]
    public async Task Concurrent_begin_has_exactly_one_owner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var firstStore = new IdempotencyStore(firstContext, _clock);
        var secondStore = new IdempotencyStore(secondContext, _clock);

        var results = await Task.WhenAll(
            firstStore.TryBeginAsync("racing-key", "orders", "same-hash", cancellationToken),
            secondStore.TryBeginAsync("racing-key", "orders", "same-hash", cancellationToken));

        results.Count(result => result.Outcome == IdempotencyOutcome.Proceed).ShouldBe(1);
        results.Count(result => result.Outcome == IdempotencyOutcome.InFlight).ShouldBe(1);
    }

    [Fact]
    public async Task Reclaimed_lease_fences_stale_complete_and_abandon()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);
        var clock = new AdjustableClock(InitialTime);
        var lifetime = TimeSpan.FromMinutes(5);

        await using var staleContext = CreateContext();
        var staleStore = new IdempotencyStore(staleContext, clock, lifetime);
        (await staleStore.TryBeginAsync("lease-key", "orders", "hash", cancellationToken))
            .Outcome.ShouldBe(IdempotencyOutcome.Proceed);

        clock.Advance(TimeSpan.FromMinutes(6));
        await using var currentContext = CreateContext();
        var currentStore = new IdempotencyStore(currentContext, clock, lifetime);
        (await currentStore.TryBeginAsync("lease-key", "orders", "hash", cancellationToken))
            .Outcome.ShouldBe(IdempotencyOutcome.Proceed);

        await Should.ThrowAsync<InvalidOperationException>(() => staleStore.CompleteAsync(
            "lease-key",
            "orders",
            "{\"statusCode\":409,\"body\":\"stale\"}",
            cancellationToken));
        await staleStore.AbandonAsync("lease-key", "orders", cancellationToken);

        const string currentSnapshot = "{\"statusCode\":202,\"body\":\"current\"}";
        await currentStore.CompleteAsync(
            "lease-key",
            "orders",
            currentSnapshot,
            cancellationToken);

        await using var replayContext = CreateContext();
        var replayStore = new IdempotencyStore(replayContext, clock, lifetime);
        var replay = await replayStore.TryBeginAsync(
            "lease-key",
            "orders",
            "hash",
            cancellationToken);
        replay.Outcome.ShouldBe(IdempotencyOutcome.AlreadyCompleted);
        replay.cachedResponse.ShouldBe(currentSnapshot);
    }

    [Fact]
    public async Task Abandoned_attempt_can_be_retried_with_the_same_payload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);

        await using (var abandonedContext = CreateContext())
        {
            var abandoned = new IdempotencyStore(abandonedContext, _clock);
            (await abandoned.TryBeginAsync("abandon-key", "orders", "hash", cancellationToken))
                .Outcome.ShouldBe(IdempotencyOutcome.Proceed);
            await abandoned.AbandonAsync("abandon-key", "orders", cancellationToken);
        }

        await using var retryContext = CreateContext();
        var retry = new IdempotencyStore(retryContext, _clock);
        (await retry.TryBeginAsync("abandon-key", "orders", "hash", cancellationToken))
            .Outcome.ShouldBe(IdempotencyOutcome.Proceed);
    }

    [Fact]
    public async Task Cancel_and_cancel_all_are_repeatable_no_ops_for_non_pending_timers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);
        Guid cancelledId;
        Guid siblingId;
        Guid firedId;
        var firedTenant = new TenantId(new Guid("00000000-0000-0000-0000-000000000099"));

        await using (var schedulerContext = CreateContext())
        {
            var scheduler = new SagaTimerScheduler(schedulerContext, _clock);
            cancelledId = await scheduler.ScheduleAsync(
                PassiveSagaHandler.SagaType,
                "order-1",
                _clock.UtcNow.AddMinutes(5),
                "{\"kind\":\"cancelled\"}",
                TenantId.Default,
                cancellationToken);
            siblingId = await scheduler.ScheduleAsync(
                PassiveSagaHandler.SagaType,
                "order-1",
                _clock.UtcNow.AddMinutes(5),
                "{\"kind\":\"sibling\"}",
                TenantId.Default,
                cancellationToken);
            firedId = await scheduler.ScheduleAsync(
                PassiveSagaHandler.SagaType,
                "order-2",
                _clock.UtcNow,
                "{\"kind\":\"fired\"}",
                firedTenant,
                cancellationToken);

            await scheduler.CancelAsync(cancelledId, cancellationToken);
            await scheduler.CancelAsync(cancelledId, cancellationToken);
            await scheduler.CancelAsync(Guid.CreateVersion7(), cancellationToken);
            await scheduler.CancelAllForSagaAsync(
                PassiveSagaHandler.SagaType,
                "order-1",
                cancellationToken);
        }

        var handler = new PassiveSagaHandler(_correlationContext);
        await using var services = new ServiceCollection()
            .AddSingleton(handler)
            .BuildServiceProvider();
        await using (var dispatcherContext = CreateContext())
        {
            var dispatcher = CreateDispatcher<PassiveSagaHandler>(
                dispatcherContext,
                services);
            (await dispatcher.DispatchDueAsync(10, cancellationToken)).ShouldBe(1);
        }

        await using (var schedulerContext = CreateContext())
        {
            var scheduler = new SagaTimerScheduler(schedulerContext, _clock);
            await scheduler.CancelAsync(firedId, cancellationToken);
            await scheduler.CancelAllForSagaAsync(
                PassiveSagaHandler.SagaType,
                "order-2",
                cancellationToken);
        }

        (await ReadTimerStateAsync(cancelledId, cancellationToken)).ShouldBe((false, true));
        (await ReadTimerStateAsync(siblingId, cancellationToken)).ShouldBe((false, true));
        (await ReadTimerStateAsync(firedId, cancellationToken)).ShouldBe((true, false));
        handler.Count.ShouldBe(1);
        handler.ObservedTenantId.ShouldBe(firedTenant);
    }

    [Fact]
    public async Task Two_workers_only_one_dispatches_a_due_timer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);
        await ScheduleDueTimerAsync(BlockingSagaHandler.SagaType, cancellationToken);

        var handler = new BlockingSagaHandler();
        await using var services = new ServiceCollection()
            .AddSingleton(handler)
            .BuildServiceProvider();
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var first = CreateDispatcher<BlockingSagaHandler>(firstContext, services);
        var second = CreateDispatcher<BlockingSagaHandler>(secondContext, services);

        var firstDispatch = first.DispatchDueAsync(10, cancellationToken);
        await handler.Started.Task.WaitAsync(cancellationToken);
        (await second.DispatchDueAsync(10, cancellationToken)).ShouldBe(0);
        handler.Release.TrySetResult();

        (await firstDispatch).ShouldBe(1);
        handler.Count.ShouldBe(1);
        (await CountFiredTimersAsync(cancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task Handler_failure_rolls_back_timer_and_releases_advisory_lock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);
        await ScheduleDueTimerAsync(FailOnceSagaHandler.SagaType, cancellationToken);

        var handler = new FailOnceSagaHandler();
        await using var services = new ServiceCollection()
            .AddSingleton(handler)
            .BuildServiceProvider();
        await using (var firstContext = CreateContext())
        {
            var first = CreateDispatcher<FailOnceSagaHandler>(firstContext, services);
            await Should.ThrowAsync<InvalidOperationException>(() =>
                first.DispatchDueAsync(10, cancellationToken));
        }

        (await CountFiredTimersAsync(cancellationToken)).ShouldBe(0);

        await using (var retryContext = CreateContext())
        {
            var retry = CreateDispatcher<FailOnceSagaHandler>(retryContext, services);
            (await retry.DispatchDueAsync(10, cancellationToken)).ShouldBe(1);
        }

        handler.Count.ShouldBe(2);
        (await CountFiredTimersAsync(cancellationToken)).ShouldBe(1);
    }

    private SagaTimerDispatcher CreateDispatcher<THandler>(
        PlatformDbContext dbContext,
        IServiceProvider serviceProvider)
        where THandler : class, ISagaTimeoutHandler =>
        new(
            dbContext,
            serviceProvider,
            [SagaTimeoutHandlerRegistration.For<THandler>()],
            _correlationContext,
            _clock,
            NullLogger<SagaTimerDispatcher>.Instance);

    private async Task ScheduleDueTimerAsync(
        string sagaType,
        CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        var scheduler = new SagaTimerScheduler(context, _clock);
        await scheduler.ScheduleAsync(
            sagaType,
            Guid.CreateVersion7().ToString("N"),
            _clock.UtcNow,
            "{\"timeout\":true}",
            TenantId.Default,
            cancellationToken);
    }

    private async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        _clock.Set(InitialTime);
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(DatabaseSchema, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<(bool Fired, bool Cancelled)> ReadTimerStateAsync(
        Guid timerId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT fired_at IS NOT NULL, cancelled_at IS NOT NULL
            FROM platform.saga_timer
            WHERE id = @id;
            """, connection);
        command.Parameters.AddWithValue("id", timerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).ShouldBeTrue();
        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }

    private async Task<int> CountFiredTimersAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT count(*)::int
            FROM platform.saga_timer
            WHERE fired_at IS NOT NULL;
            """, connection);
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private PlatformDbContext CreateContext() => new(
        new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);
}

internal sealed class AdjustableClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = utcNow;

    public DateOnly TodayInTaipei => DateOnly.FromDateTime(UtcNow.AddHours(8).DateTime);

    public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);

    public void Set(DateTimeOffset value) => UtcNow = value;
}

internal sealed class PassiveSagaHandler(CorrelationContext correlationContext)
    : ISagaTimeoutHandler
{
    public static string SagaType => "test.passive";

    public int Count { get; private set; }

    public TenantId? ObservedTenantId { get; private set; }

    public Task HandleTimeoutAsync(
        string sagaId,
        string payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Count++;
        ObservedTenantId = correlationContext.TenantId;
        return Task.CompletedTask;
    }
}

internal sealed class BlockingSagaHandler : ISagaTimeoutHandler
{
    public static string SagaType => "test.blocking";

    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Count { get; private set; }

    public async Task HandleTimeoutAsync(
        string sagaId,
        string payload,
        CancellationToken cancellationToken)
    {
        Count++;
        Started.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
    }
}

internal sealed class FailOnceSagaHandler : ISagaTimeoutHandler
{
    public static string SagaType => "test.fail-once";

    private int _count;

    public int Count => _count;

    public Task HandleTimeoutAsync(
        string sagaId,
        string payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _count) == 1)
        {
            throw new InvalidOperationException("expected first-attempt failure");
        }

        return Task.CompletedTask;
    }
}
