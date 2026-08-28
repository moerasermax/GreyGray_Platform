using System.Collections.Concurrent;
using System.Diagnostics;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Observability;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.Platform.Tests;

public sealed class OutboxDatabaseTests : IAsyncLifetime
{
    private const string DatabaseSchema = """
        DROP SCHEMA IF EXISTS platform CASCADE;
        DROP TABLE IF EXISTS public.probe_business;

        CREATE SCHEMA platform;

        CREATE TABLE platform.outbox_message (
            id              uuid        PRIMARY KEY,
            tenant_id       uuid        NOT NULL,
            aggregate_type  text        NOT NULL,
            aggregate_id    text        NOT NULL,
            event_type      text        NOT NULL,
            payload         jsonb       NOT NULL,
            occurred_at     timestamptz NOT NULL,
            correlation_id  text        NOT NULL,
            causation_id    text,
            processed_at    timestamptz,
            attempts        int         NOT NULL DEFAULT 0,
            next_attempt_at timestamptz NOT NULL DEFAULT now(),
            last_error      text,
            dead_lettered   boolean     NOT NULL DEFAULT false,
            CONSTRAINT outbox_attempts_non_negative CHECK (attempts >= 0)
        );

        CREATE INDEX ix_outbox_pending
            ON platform.outbox_message (next_attempt_at)
            WHERE processed_at IS NULL AND dead_lettered = false;
        CREATE INDEX ix_outbox_aggregate
            ON platform.outbox_message (aggregate_type, aggregate_id, occurred_at DESC);
        CREATE INDEX ix_outbox_dead_letter
            ON platform.outbox_message (occurred_at DESC)
            WHERE dead_lettered = true;

        CREATE TABLE public.probe_business (
            id uuid PRIMARY KEY
        );
        """;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();
    private readonly ActivityListener _activityListener = new()
    {
        ShouldListenTo = source => source.Name == GreyGrayTelemetry.ActivitySourceName,
        Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
            ActivitySamplingResult.AllData,
    };
    private readonly EventTypeRegistry _eventTypes =
        EventTypeRegistry.FromAssemblies(EventCatalog.ContractAssemblies);
    private readonly FakeClock _clock = new(TestEvents.OccurredAt);

    public async ValueTask InitializeAsync()
    {
        ActivitySource.AddActivityListener(_activityListener);
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _activityListener.Dispose();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Business_write_and_outbox_are_both_removed_by_transaction_rollback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);

        var options = BusinessOptions();
        var eventId = Guid.CreateVersion7();
        await using (var dbContext = new ProbeBusinessDbContext(options))
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            dbContext.BusinessRows.Add(new ProbeBusinessRow { Id = Guid.CreateVersion7() });
            var publisher = new OutboxEventPublisher<ProbeBusinessDbContext>(
                dbContext,
                new CorrelationContext(),
                _eventTypes);

            await publisher.PublishAsync(TestEvents.CustomerRegistered(eventId), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
        }

        await using var verification = new ProbeBusinessDbContext(options);
        (await verification.BusinessRows.CountAsync(cancellationToken)).ShouldBe(0);
        (await verification.Set<OutboxMessage>().CountAsync(cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Two_dispatchers_process_100_messages_once_each()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);
        await PublishMessagesAsync(100, cancellationToken);

        var handler = new CountingCustomerRegisteredHandler();
        await using var services = new ServiceCollection()
            .AddSingleton<IIntegrationEventHandler<CustomerRegistered>>(handler)
            .BuildServiceProvider();

        await using var firstContext = new PlatformDbContext(PlatformOptions());
        await using var secondContext = new PlatformDbContext(PlatformOptions());
        var first = CreateDispatcher(firstContext, services);
        var second = CreateDispatcher(secondContext, services);

        var results = await Task.WhenAll(
            first.DispatchBatchAsync(100, cancellationToken),
            second.DispatchBatchAsync(100, cancellationToken));

        results.Sum().ShouldBe(100);
        handler.Counts.Count.ShouldBe(100);
        handler.Counts.Values.ShouldAllBe(count => count == 1);

        await using var verification = new PlatformDbContext(PlatformOptions());
        (await verification.OutboxMessages.CountAsync(
                message => message.ProcessedAt != null,
                cancellationToken))
            .ShouldBe(100);
    }

    [Fact]
    public async Task Handler_failure_increments_attempts_and_schedules_retry_without_processing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);
        await PublishMessagesAsync(1, cancellationToken);

        await using var services = new ServiceCollection()
            .AddSingleton<IIntegrationEventHandler<CustomerRegistered>>(
                new ThrowingCustomerRegisteredHandler())
            .BuildServiceProvider();

        await using var dispatcherContext = new PlatformDbContext(PlatformOptions());
        var dispatcher = CreateDispatcher(dispatcherContext, services);
        (await dispatcher.DispatchBatchAsync(10, cancellationToken)).ShouldBe(0);

        await using var verification = new PlatformDbContext(PlatformOptions());
        var message = await verification.OutboxMessages
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        message.Attempts.ShouldBe(1);
        message.NextAttemptAt.ShouldBeGreaterThan(_clock.UtcNow);
        message.ProcessedAt.ShouldBeNull();
        message.IsDeadLettered.ShouldBeFalse();
        message.LastError.ShouldNotBeNull();
        message.LastError!.ShouldContain("probe handler failure");
    }

    private OutboxDispatcher CreateDispatcher(
        PlatformDbContext dbContext,
        IServiceProvider serviceProvider) => new(
            dbContext,
            serviceProvider,
            _eventTypes,
            new CorrelationContext(),
            _clock,
            NullLogger<OutboxDispatcher>.Instance);

    private async Task PublishMessagesAsync(int count, CancellationToken cancellationToken)
    {
        await using var dbContext = new ProbeBusinessDbContext(BusinessOptions());
        var publisher = new OutboxEventPublisher<ProbeBusinessDbContext>(
            dbContext,
            new CorrelationContext(),
            _eventTypes);

        for (var index = 0; index < count; index++)
        {
            await publisher.PublishAsync(
                TestEvents.CustomerRegistered(Guid.CreateVersion7()),
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(DatabaseSchema, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbContextOptions<ProbeBusinessDbContext> BusinessOptions() =>
        new DbContextOptionsBuilder<ProbeBusinessDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

    private DbContextOptions<PlatformDbContext> PlatformOptions() =>
        new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
}

internal sealed class ProbeBusinessDbContext(
    DbContextOptions<ProbeBusinessDbContext> options) : DbContext(options)
{
    public DbSet<ProbeBusinessRow> BusinessRows => Set<ProbeBusinessRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProbeBusinessRow>(entity =>
        {
            entity.ToTable("probe_business", "public");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        });
        modelBuilder.AddPlatformTables();
    }
}

internal sealed class ProbeBusinessRow
{
    public required Guid Id { get; init; }
}

internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
    public DateOnly TodayInTaipei => DateOnly.FromDateTime(UtcNow.AddHours(8).DateTime);
}

internal sealed class CountingCustomerRegisteredHandler
    : IIntegrationEventHandler<CustomerRegistered>
{
    public ConcurrentDictionary<Guid, int> Counts { get; } = new();

    public Task HandleAsync(CustomerRegistered @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Counts.AddOrUpdate(@event.EventId, 1, static (_, count) => count + 1);
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingCustomerRegisteredHandler
    : IIntegrationEventHandler<CustomerRegistered>
{
    public Task HandleAsync(CustomerRegistered @event, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("probe handler failure");
}
