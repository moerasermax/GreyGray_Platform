using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.Platform.Tests;

public sealed class ProcessedMessageTests : IAsyncLifetime
{
    private const string DatabaseSchema = """
        DROP SCHEMA IF EXISTS platform CASCADE;
        DROP TABLE IF EXISTS public.processed_message_effect;

        CREATE SCHEMA platform;

        CREATE TABLE platform.processed_message (
            event_id     uuid        NOT NULL,
            handler_name text        NOT NULL,
            processed_at timestamptz NOT NULL DEFAULT now(),
            PRIMARY KEY (event_id, handler_name)
        );

        CREATE TABLE public.processed_message_effect (
            id           uuid PRIMARY KEY,
            event_id     uuid NOT NULL,
            handler_name text NOT NULL
        );
        """;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();
    private readonly FakeClock _clock = new(TestEvents.OccurredAt.AddMinutes(15));

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact]
    public async Task Same_event_and_handler_are_applied_once_when_dispatched_twice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);
        var @event = TestEvents.CustomerRegistered(Guid.CreateVersion7());

        await using var services = BuildServices(collection =>
            collection.AddIdempotentIntegrationEventHandler<
                CustomerRegistered,
                FirstSideEffectHandler,
                ProcessedMessageProbeDbContext>());

        await DispatchAsync(services, @event, cancellationToken);
        await DispatchAsync(services, @event, cancellationToken);

        await using var verification = CreateDbContext();
        (await verification.Effects.CountAsync(cancellationToken)).ShouldBe(1);
        (await verification.Set<ProcessedMessage>().CountAsync(cancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_scopes_competing_for_the_same_key_apply_the_effect_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);
        var @event = TestEvents.CustomerRegistered(Guid.CreateVersion7());

        await using var services = BuildServices(collection =>
            collection.AddIdempotentIntegrationEventHandler<
                CustomerRegistered,
                FirstSideEffectHandler,
                ProcessedMessageProbeDbContext>());

        await Task.WhenAll(
            DispatchAsync(services, @event, cancellationToken),
            DispatchAsync(services, @event, cancellationToken));

        await using var verification = CreateDbContext();
        (await verification.Effects.CountAsync(cancellationToken)).ShouldBe(1);
        (await verification.Set<ProcessedMessage>().CountAsync(cancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task Same_event_is_applied_once_per_distinct_handler()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);
        var @event = TestEvents.CustomerRegistered(Guid.CreateVersion7());

        await using var services = BuildServices(collection =>
        {
            collection.AddIdempotentIntegrationEventHandler<
                CustomerRegistered,
                FirstSideEffectHandler,
                ProcessedMessageProbeDbContext>();
            collection.AddIdempotentIntegrationEventHandler<
                CustomerRegistered,
                SecondSideEffectHandler,
                ProcessedMessageProbeDbContext>();
        });

        await DispatchAsync(services, @event, cancellationToken);
        await DispatchAsync(services, @event, cancellationToken);

        await using var verification = CreateDbContext();
        var effects = await verification.Effects
            .AsNoTracking()
            .Where(effect => effect.EventId == @event.EventId)
            .ToListAsync(cancellationToken);
        effects.Count.ShouldBe(2);
        effects.Select(effect => effect.HandlerName)
            .ToHashSet(StringComparer.Ordinal)
            .SetEquals([FirstSideEffectHandler.Name, SecondSideEffectHandler.Name])
            .ShouldBeTrue();
        (await verification.Set<ProcessedMessage>().CountAsync(cancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task Handler_failure_rolls_back_marker_and_side_effect_then_allows_retry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);
        var @event = TestEvents.CustomerRegistered(Guid.CreateVersion7());

        await using var services = BuildServices(collection =>
        {
            collection.AddSingleton<FailOnceGate>();
            collection.AddIdempotentIntegrationEventHandler<
                CustomerRegistered,
                FailOnceSideEffectHandler,
                ProcessedMessageProbeDbContext>();
        });

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            DispatchAsync(services, @event, cancellationToken));
        exception.Message.ShouldBe("injected handler failure");

        await using (var afterFailure = CreateDbContext())
        {
            (await afterFailure.Effects.CountAsync(cancellationToken)).ShouldBe(0);
            (await afterFailure.Set<ProcessedMessage>().CountAsync(cancellationToken)).ShouldBe(0);
        }

        await DispatchAsync(services, @event, cancellationToken);

        await using var afterRetry = CreateDbContext();
        (await afterRetry.Effects.CountAsync(cancellationToken)).ShouldBe(1);
        (await afterRetry.Set<ProcessedMessage>().CountAsync(cancellationToken)).ShouldBe(1);
    }

    private ServiceProvider BuildServices(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection()
            .AddSingleton<IClock>(_clock)
            .AddDbContext<ProcessedMessageProbeDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()));
        configure(services);
        return services.BuildServiceProvider();
    }

    private static async Task DispatchAsync(
        IServiceProvider services,
        CustomerRegistered @event,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        foreach (var handler in scope.ServiceProvider
                     .GetServices<IIntegrationEventHandler<CustomerRegistered>>())
        {
            await handler.HandleAsync(@event, cancellationToken);
        }
    }

    private async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(DatabaseSchema, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private ProcessedMessageProbeDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ProcessedMessageProbeDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);
}

internal sealed class ProcessedMessageProbeDbContext(
    DbContextOptions<ProcessedMessageProbeDbContext> options) : DbContext(options)
{
    public DbSet<ProcessedMessageEffect> Effects => Set<ProcessedMessageEffect>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedMessageEffect>(entity =>
        {
            entity.ToTable("processed_message_effect", "public");
            entity.HasKey(effect => effect.Id);
            entity.Property(effect => effect.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(effect => effect.EventId).HasColumnName("event_id");
            entity.Property(effect => effect.HandlerName).HasColumnName("handler_name");
        });
        modelBuilder.AddPlatformTables();
    }
}

internal sealed class ProcessedMessageEffect
{
    public required Guid Id { get; init; }
    public required Guid EventId { get; init; }
    public required string HandlerName { get; init; }
}

internal sealed class FirstSideEffectHandler(ProcessedMessageProbeDbContext dbContext)
    : IIntegrationEventHandler<CustomerRegistered>
{
    public const string Name = "first";

    public Task HandleAsync(CustomerRegistered @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        dbContext.Effects.Add(NewEffect(@event.EventId, Name));
        return Task.CompletedTask;
    }

    internal static ProcessedMessageEffect NewEffect(Guid eventId, string handlerName) => new()
    {
        Id = Guid.CreateVersion7(),
        EventId = eventId,
        HandlerName = handlerName,
    };
}

internal sealed class SecondSideEffectHandler(ProcessedMessageProbeDbContext dbContext)
    : IIntegrationEventHandler<CustomerRegistered>
{
    public const string Name = "second";

    public Task HandleAsync(CustomerRegistered @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        dbContext.Effects.Add(FirstSideEffectHandler.NewEffect(@event.EventId, Name));
        return Task.CompletedTask;
    }
}

internal sealed class FailOnceSideEffectHandler(
    ProcessedMessageProbeDbContext dbContext,
    FailOnceGate gate) : IIntegrationEventHandler<CustomerRegistered>
{
    public async Task HandleAsync(
        CustomerRegistered @event,
        CancellationToken cancellationToken)
    {
        dbContext.Effects.Add(FirstSideEffectHandler.NewEffect(@event.EventId, "fail-once"));
        await dbContext.SaveChangesAsync(cancellationToken);

        if (gate.ShouldFail())
        {
            throw new InvalidOperationException("injected handler failure");
        }
    }
}

internal sealed class FailOnceGate
{
    private int _attempts;

    public bool ShouldFail() => Interlocked.Increment(ref _attempts) == 1;
}
