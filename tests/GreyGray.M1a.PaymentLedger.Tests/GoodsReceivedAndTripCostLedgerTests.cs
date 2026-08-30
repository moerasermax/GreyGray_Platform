using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ledger.Contracts;
using GreyGray.Modules.Ledger.Infra;
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

namespace GreyGray.M1a.PaymentLedger.Tests;

/// <summary>
/// M1b-3b：Ledger 訂閱 <c>procurement.GoodsReceived.v1</c> 與 <c>campaign.TripCostRecorded.v1</c>。
/// 兩個事件都是既成事實（BE-12 已通過驗收），這裡直接建構事件送進 handler。
/// </summary>
public sealed class GoodsReceivedAndTripCostLedgerTests : IAsyncLifetime
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

    [Fact(DisplayName = "GoodsReceived 記 DR 存貨 / CR 現金，借貸相等且重送只入帳一次")]
    public async Task GoodsReceived_posts_balanced_entry_and_is_idempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        var campaignId = CampaignId.New();
        await InsertCampaignAsync(campaignId, cancellationToken);
        await using var provider = BuildProvider();

        var received = new GoodsReceived(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            campaignId,
            SkuId.New(),
            5,
            Money.OfMajor(350, Currency.TWD),
            LotSource.OverseasPurchase,
            OrderLineId.New());

        await DispatchAsync(provider, received, cancellationToken);
        await DispatchAsync(provider, received, cancellationToken);

        (await CountEntriesAsync("Procurement", received.EventId.ToString(), cancellationToken)).ShouldBe(1);
        (await DebitTotalAsync(AccountCodes.Inventory, cancellationToken)).ShouldBe(175_000);
        (await CreditTotalAsync(AccountCodes.Cash, cancellationToken)).ShouldBe(175_000);
        (await CountProcessedAsync(received.EventId, cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "TripCostRecorded 記 DR 旅程成本 / CR 現金，借貸相等且重送只入帳一次")]
    public async Task TripCostRecorded_posts_balanced_entry_and_is_idempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        var campaignId = CampaignId.New();
        await InsertCampaignAsync(campaignId, cancellationToken);
        await using var provider = BuildProvider();

        var tripCostId = TripCostId.New();
        var recorded = new TripCostRecorded(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            campaignId,
            tripCostId,
            TripCostKind.Airfare,
            Money.OfMajor(12_000, Currency.TWD),
            "來回機票");

        await DispatchAsync(provider, recorded, cancellationToken);
        await DispatchAsync(provider, recorded, cancellationToken);

        (await CountEntriesAsync("Campaign", tripCostId.ToString(), cancellationToken)).ShouldBe(1);
        (await DebitTotalAsync(AccountCodes.TripCost, cancellationToken)).ShouldBe(1_200_000);
        (await CreditTotalAsync(AccountCodes.Cash, cancellationToken)).ShouldBe(1_200_000);
    }

    private ServiceProvider BuildProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GreyGray_ledger"] = _postgres.GetConnectionString(),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new StubClock(Now));
        services.AddSingleton<ICorrelationContext>(new CorrelationContext());
        services.AddSingleton(EventTypeRegistry.FromAssemblies([
            typeof(JournalPosted).Assembly,
            typeof(GoodsReceived).Assembly,
            typeof(TripCostRecorded).Assembly,
        ]));
        services.AddLedgerModule(configuration);
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
                ledger.journal_line,
                ledger.journal_entry,
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

    private Task<int> CountEntriesAsync(
        string sourceModule,
        string sourceRef,
        CancellationToken cancellationToken) =>
        ScalarAsync<int>("""
            SELECT count(*)::integer FROM ledger.journal_entry
            WHERE source_module = @source_module AND source_ref = @source_ref;
            """, cancellationToken,
            new NpgsqlParameter("source_module", sourceModule),
            new NpgsqlParameter("source_ref", sourceRef));

    private Task<long> DebitTotalAsync(string accountCode, CancellationToken cancellationToken) =>
        ScalarAsync<long>("""
            SELECT coalesce(sum(amount_minor), 0)::bigint FROM ledger.journal_line
            WHERE account_code = @account_code AND direction = 1;
            """, cancellationToken, new NpgsqlParameter("account_code", accountCode));

    private Task<long> CreditTotalAsync(string accountCode, CancellationToken cancellationToken) =>
        ScalarAsync<long>("""
            SELECT coalesce(sum(amount_minor), 0)::bigint FROM ledger.journal_line
            WHERE account_code = @account_code AND direction = 2;
            """, cancellationToken, new NpgsqlParameter("account_code", accountCode));

    private Task<int> CountProcessedAsync(Guid eventId, CancellationToken cancellationToken) =>
        ScalarAsync<int>("SELECT count(*)::integer FROM platform.processed_message WHERE event_id = @event_id;",
            cancellationToken, new NpgsqlParameter("event_id", eventId));

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

    private sealed class StubClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;

        public DateOnly TodayInTaipei => DateOnly.FromDateTime(now.UtcDateTime.AddHours(8));
    }
}
