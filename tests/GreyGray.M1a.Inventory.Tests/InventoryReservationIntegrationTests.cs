using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Inventory.Infra;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Observability;
using GreyGray.Shared.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1a.Inventory.Tests;

public sealed class InventoryReservationIntegrationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await ApplyMigrationsAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "PostgreSQL reservation 防超賣、冪等、釋放，事件重送只處理一次")]
    public async Task Reservation_and_order_handlers_preserve_inventory_invariants()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        var directSku = SkuId.New();
        await InsertLotAsync(directSku, 10, cancellationToken);

        Result<ReservationId> first;
        Result<ReservationId> replay;
        await using (var scope = provider.CreateAsyncScope())
        {
            var reservations = scope.ServiceProvider.GetRequiredService<IStockReservation>();
            first = await reservations.ReserveAsync("direct-1", [(directSku, 6)], cancellationToken);
            replay = await reservations.ReserveAsync("direct-1", [(directSku, 6)], cancellationToken);
        }

        first.IsSuccess.ShouldBeTrue();
        replay.Value.ShouldBe(first.Value);
        (await ReservedAsync(directSku, cancellationToken)).ShouldBe(6);
        (await CountAsync("inventory.reservation", cancellationToken)).ShouldBe(1);
        (await CountOutboxAsync(StockReserved.EventType, cancellationToken)).ShouldBe(1);

        var concurrentSku = SkuId.New();
        await InsertLotAsync(concurrentSku, 10, cancellationToken);
        Result<ReservationId>[] competing;
        await using (var firstScope = provider.CreateAsyncScope())
        await using (var secondScope = provider.CreateAsyncScope())
        {
            competing = await Task.WhenAll(
                firstScope.ServiceProvider.GetRequiredService<IStockReservation>()
                    .ReserveAsync("concurrent-1", [(concurrentSku, 6)], cancellationToken),
                secondScope.ServiceProvider.GetRequiredService<IStockReservation>()
                    .ReserveAsync("concurrent-2", [(concurrentSku, 6)], cancellationToken));
        }

        competing.Count(result => result.IsSuccess).ShouldBe(1);
        competing.Count(result => result.IsFailure).ShouldBe(1);
        competing.Single(result => result.IsFailure).Error.Code
            .ShouldBe("inventory.insufficient-stock");
        (await ReservedAsync(concurrentSku, cancellationToken)).ShouldBe(6);

        await using (var scope = provider.CreateAsyncScope())
        {
            var reservations = scope.ServiceProvider.GetRequiredService<IStockReservation>();
            (await reservations.ReleaseAsync(first.Value, cancellationToken)).IsSuccess.ShouldBeTrue();
            (await reservations.ReleaseAsync(first.Value, cancellationToken)).IsSuccess.ShouldBeTrue();
        }

        (await ReservedAsync(directSku, cancellationToken)).ShouldBe(0);
        (await CountOutboxAsync(StockReleased.EventType, cancellationToken)).ShouldBe(1);

        var stockSku = SkuId.New();
        var preorderSku = SkuId.New();
        await InsertLotAsync(stockSku, 10, cancellationToken);
        var orderId = OrderId.New();
        var placed = new OrderPlaced(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            orderId,
            CustomerId.New(),
            SourceChannel.Own,
            ShippingPolicy.HoldUntilComplete,
            PricingSnapshotId.New(),
            Money.OfMajor(40, Currency.TWD),
            Money.Zero(Currency.TWD),
            [
                new OrderPlacedLine(
                    OrderLineId.New(), stockSku, FulfillmentMode.Stock,
                    null, null, 4, Money.OfMajor(10, Currency.TWD)),
                new OrderPlacedLine(
                    OrderLineId.New(), preorderSku, FulfillmentMode.Preorder,
                    null, null, 99, Money.OfMajor(10, Currency.TWD)),
            ]);

        await DispatchAsync(provider, placed, cancellationToken);
        await DispatchAsync(provider, placed, cancellationToken);

        (await ReservedAsync(stockSku, cancellationToken)).ShouldBe(4);
        (await CountAllocationsAsync(stockSku, cancellationToken)).ShouldBe(1);
        (await CountAllocationsAsync(preorderSku, cancellationToken)).ShouldBe(0);
        (await CountProcessedAsync(placed.EventId, cancellationToken)).ShouldBe(1);

        var cancelled = new OrderCancelled(
            Guid.CreateVersion7(),
            Now.AddMinutes(1),
            TenantId.Default,
            orderId,
            "customer cancelled",
            Money.OfMajor(40, Currency.TWD),
            RefundDestination.StoredValue);
        await DispatchAsync(provider, cancelled, cancellationToken);
        await DispatchAsync(provider, cancelled, cancellationToken);

        (await ReservedAsync(stockSku, cancellationToken)).ShouldBe(0);
        (await CountProcessedAsync(cancelled.EventId, cancellationToken)).ShouldBe(1);
        (await ReservationStatusAsync(orderId, cancellationToken)).ShouldBe((short)1);
    }

    private ServiceProvider BuildProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GreyGray_inventory"] = _postgres.GetConnectionString(),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new StubClock(Now));
        services.AddSingleton<ICorrelationContext>(new CorrelationContext());
        services.AddSingleton(EventTypeRegistry.FromAssemblies([typeof(StockReserved).Assembly]));
        services.AddInventoryModule(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task DispatchAsync<TEvent>(
        ServiceProvider provider,
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        await using var scope = provider.CreateAsyncScope();
        foreach (var handler in scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            await handler.HandleAsync(@event, cancellationToken);
        }
    }

    private async Task ResetAsync(CancellationToken cancellationToken) =>
        await ExecuteAsync("""
            TRUNCATE TABLE
                inventory.reservation_allocation,
                inventory.reservation,
                inventory.lot,
                platform.outbox_message,
                platform.processed_message
            CASCADE;
            """, cancellationToken);

    private async Task InsertLotAsync(
        SkuId skuId,
        int quantity,
        CancellationToken cancellationToken) =>
        await ExecuteAsync("""
            INSERT INTO inventory.lot (
                id, tenant_id, sku_id, quantity_on_hand, quantity_reserved)
            VALUES (@id, @tenant_id, @sku_id, @quantity, 0);
            """, cancellationToken,
            new NpgsqlParameter("id", Guid.CreateVersion7()),
            new NpgsqlParameter("tenant_id", TenantId.Default.Value),
            new NpgsqlParameter("sku_id", skuId.Value),
            new NpgsqlParameter("quantity", quantity));

    private Task<int> ReservedAsync(SkuId skuId, CancellationToken cancellationToken) =>
        ScalarAsync<int>("""
            SELECT quantity_reserved
            FROM inventory.lot
            WHERE tenant_id = @tenant_id AND sku_id = @sku_id;
            """, cancellationToken,
            new NpgsqlParameter("tenant_id", TenantId.Default.Value),
            new NpgsqlParameter("sku_id", skuId.Value));

    private Task<int> CountAsync(string table, CancellationToken cancellationToken) =>
        ScalarAsync<int>($"SELECT count(*)::integer FROM {table};", cancellationToken);

    private Task<int> CountOutboxAsync(string eventType, CancellationToken cancellationToken) =>
        ScalarAsync<int>("SELECT count(*)::integer FROM platform.outbox_message WHERE event_type = @event_type;",
            cancellationToken, new NpgsqlParameter("event_type", eventType));

    private Task<int> CountAllocationsAsync(SkuId skuId, CancellationToken cancellationToken) =>
        ScalarAsync<int>("""
            SELECT count(*)::integer
            FROM inventory.reservation_allocation
            WHERE tenant_id = @tenant_id AND sku_id = @sku_id;
            """, cancellationToken,
            new NpgsqlParameter("tenant_id", TenantId.Default.Value),
            new NpgsqlParameter("sku_id", skuId.Value));

    private Task<int> CountProcessedAsync(Guid eventId, CancellationToken cancellationToken) =>
        ScalarAsync<int>("SELECT count(*)::integer FROM platform.processed_message WHERE event_id = @event_id;",
            cancellationToken, new NpgsqlParameter("event_id", eventId));

    private Task<short> ReservationStatusAsync(OrderId orderId, CancellationToken cancellationToken) =>
        ScalarAsync<short>("""
            SELECT status
            FROM inventory.reservation
            WHERE tenant_id = @tenant_id AND reservation_key = @reservation_key;
            """, cancellationToken,
            new NpgsqlParameter("tenant_id", TenantId.Default.Value),
            new NpgsqlParameter("reservation_key", $"ordering:{orderId}"));

    private async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        // 上界從 db/migrations/ 的實際內容推導，不寫死：保留庫存這條路要跑在
        // 正式機真的會有的完整 schema 上，而寫死的上界每加一份 migration 就少測一份。
        var migrationDirectory = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        var paths = Directory.GetFiles(migrationDirectory, "*.sql")
            .Where(path => int.TryParse(Path.GetFileName(path).AsSpan(0, 4), out _))
            .OrderBy(path => path, StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var sql = string.Join(
                Environment.NewLine,
                File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('\\')));
            await ExecuteAsync(sql, cancellationToken);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GreyGray.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Cannot locate GreyGray.slnx from test output directory.");
    }

    private async Task ExecuteAsync(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<T> ScalarAsync<T>(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Expected scalar value."));
    }

    private sealed class StubClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;

        public DateOnly TodayInTaipei => DateOnly.FromDateTime(now.UtcDateTime.AddHours(8));
    }
}
