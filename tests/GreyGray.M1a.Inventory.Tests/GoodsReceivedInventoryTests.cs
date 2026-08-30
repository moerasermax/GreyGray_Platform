using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Inventory.Core;
using GreyGray.Modules.Inventory.Infra;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Procurement.Contracts;
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

/// <summary>
/// M1b-3b：帶回入庫。<c>procurement.GoodsReceived.v1</c> 是既成事實（BE-12 已通過驗收）——
/// 這裡直接建構事件送進 Inventory 的 handler，不重新走 Procurement 的完整流程。
/// </summary>
public sealed class GoodsReceivedInventoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await ApplyMigrationsAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "GoodsReceived 建立新批號、記錄成本與來源團，重送不建立第二個批號")]
    public async Task GoodsReceived_creates_lot_and_is_idempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        var campaignId = CampaignId.New();
        await InsertCampaignAsync(campaignId, cancellationToken);
        await using var provider = BuildProvider();

        var skuId = SkuId.New();
        var received = new GoodsReceived(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            campaignId,
            skuId,
            5,
            Money.OfMajor(350, Currency.TWD),
            LotSource.OverseasPurchase,
            OrderLineId.New());

        await DispatchAsync(provider, received, cancellationToken);
        await DispatchAsync(provider, received, cancellationToken);

        (await CountAsync("inventory.lot", cancellationToken)).ShouldBe(1);
        (await CountOutboxAsync(LotCreated.EventType, cancellationToken)).ShouldBe(1);
        (await CountProcessedAsync(received.EventId, cancellationToken)).ShouldBe(1);

        var lot = await ReadLotAsync(skuId, cancellationToken);
        lot.QuantityOnHand.ShouldBe(5);
        lot.QuantityReserved.ShouldBe(0);
        lot.UnitCostAmountMinor.ShouldBe(35_000);
        lot.UnitCostCurrency.ShouldBe("TWD");
        lot.Source.ShouldBe((short)2);
        lot.FromCampaignId.ShouldBe(campaignId.Value);
        lot.ReceivedAt.ShouldBe(Now);
    }

    [Fact(DisplayName = "0010 可重跑兩次、owner 是 greygray_owner、group-consistency constraint 擋不完整資料")]
    public async Task Migration_0010_is_idempotent_owned_and_rejects_partial_receipt_fields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databaseName = $"m1b3b_migration_{Guid.NewGuid():N}";
        await ExecuteAsync(
            _postgres.GetConnectionString(),
            $"CREATE DATABASE \"{databaseName}\";",
            cancellationToken);
        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName,
        };
        var connectionString = builder.ConnectionString;
        var migrationPath = Path.Combine(FindRepositoryRoot(), "db", "migrations", "0010_m1b_inventory.sql");
        var sql = string.Join(
            Environment.NewLine,
            File.ReadLines(migrationPath).Where(line => !line.TrimStart().StartsWith('\\')));

        await ApplyMigrationChainUpToAsync(connectionString, 9, cancellationToken);
        await ExecuteAsync(connectionString, sql, cancellationToken);
        await ExecuteAsync(connectionString, sql, cancellationToken);

        (await ScalarAsync<string>(connectionString, """
            SELECT tableowner FROM pg_tables WHERE schemaname = 'inventory' AND tablename = 'lot';
            """, cancellationToken)).ShouldBe("greygray_owner");

        var partial = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connectionString, """
            INSERT INTO inventory.lot (id, tenant_id, sku_id, quantity_on_hand, source)
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-000000000001'::uuid, gen_random_uuid(), 1, 2);
            """, cancellationToken));
        partial.SqlState.ShouldBe("23514");
    }

    private static async Task ApplyMigrationChainUpToAsync(
        string connectionString,
        int lastMigration,
        CancellationToken cancellationToken)
    {
        var migrationDirectory = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        var paths = Directory.GetFiles(migrationDirectory, "*.sql")
            .Where(path => int.TryParse(Path.GetFileName(path).AsSpan(0, 4), out var number)
                && number <= lastMigration)
            .OrderBy(path => path, StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var sql = string.Join(
                Environment.NewLine,
                File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('\\')));
            await ExecuteAsync(connectionString, sql, cancellationToken);
        }
    }

    private static async Task ExecuteAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ScalarAsync<T>(
        string connectionString,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Expected a scalar value."));
    }

    [Fact(DisplayName = "帶回入庫的批號成本非負、幣別合法，違反規則回可預期失敗而非例外")]
    public void Invalid_goods_received_input_is_rejected_as_a_result_failure()
    {
        var invalidQuantity = LotAggregate.CreateFromGoodsReceived(
            LotId.New(),
            TenantId.Default,
            SkuId.New(),
            0,
            Money.OfMajor(100, Currency.TWD),
            LotSource.OverseasPurchase,
            CampaignId.New(),
            Now);
        invalidQuantity.IsFailure.ShouldBeTrue();
        invalidQuantity.Error.Code.ShouldBe("inventory.invalid-goods-received-quantity");

        var negativeCost = LotAggregate.CreateFromGoodsReceived(
            LotId.New(),
            TenantId.Default,
            SkuId.New(),
            1,
            Money.OfMajor(-1, Currency.TWD),
            LotSource.OverseasPurchase,
            CampaignId.New(),
            Now);
        negativeCost.IsFailure.ShouldBeTrue();
        negativeCost.Error.Code.ShouldBe("inventory.invalid-goods-received-unit-cost");
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
        services.AddSingleton(EventTypeRegistry.FromAssemblies([
            typeof(LotCreated).Assembly,
            typeof(GoodsReceived).Assembly,
        ]));
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
                campaign.campaign,
                platform.outbox_message,
                platform.processed_message
            CASCADE;
            """, cancellationToken);

    private async Task InsertCampaignAsync(CampaignId campaignId, CancellationToken cancellationToken) =>
        await ExecuteAsync("""
            INSERT INTO campaign.campaign (
                id, tenant_id, title, destination, depart_at, return_at, closes_at,
                status, created_at, updated_at)
            VALUES (
                @id, @tenant_id, '東京採購', 'Tokyo',
                current_date + 10, current_date + 12, now() + interval '5 days',
                1, now(), now());
            """, cancellationToken,
            new NpgsqlParameter("id", campaignId.Value),
            new NpgsqlParameter("tenant_id", TenantId.Default.Value));

    private Task<int> CountAsync(string table, CancellationToken cancellationToken) =>
        ScalarAsync<int>($"SELECT count(*)::integer FROM {table};", cancellationToken);

    private Task<int> CountOutboxAsync(string eventType, CancellationToken cancellationToken) =>
        ScalarAsync<int>("SELECT count(*)::integer FROM platform.outbox_message WHERE event_type = @event_type;",
            cancellationToken, new NpgsqlParameter("event_type", eventType));

    private Task<int> CountProcessedAsync(Guid eventId, CancellationToken cancellationToken) =>
        ScalarAsync<int>("SELECT count(*)::integer FROM platform.processed_message WHERE event_id = @event_id;",
            cancellationToken, new NpgsqlParameter("event_id", eventId));

    private async Task<LotRow> ReadLotAsync(SkuId skuId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT quantity_on_hand, quantity_reserved, unit_cost_amount_minor,
                   unit_cost_currency, source, from_campaign_id, received_at
            FROM inventory.lot
            WHERE sku_id = @sku_id;
            """, connection);
        command.Parameters.AddWithValue("sku_id", skuId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).ShouldBeTrue();
        return new LotRow(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetInt16(4),
            reader.GetGuid(5),
            reader.GetFieldValue<DateTimeOffset>(6));
    }

    private async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        var migrationDirectory = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        var paths = Directory.GetFiles(migrationDirectory, "*.sql")
            .Where(path => int.TryParse(Path.GetFileName(path).AsSpan(0, 4), out var number)
                && number <= 10)
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

    private sealed record LotRow(
        int QuantityOnHand,
        int QuantityReserved,
        long UnitCostAmountMinor,
        string UnitCostCurrency,
        short Source,
        Guid FromCampaignId,
        DateTimeOffset ReceivedAt);

    private sealed class StubClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;

        public DateOnly TodayInTaipei => DateOnly.FromDateTime(now.UtcDateTime.AddHours(8));
    }
}
