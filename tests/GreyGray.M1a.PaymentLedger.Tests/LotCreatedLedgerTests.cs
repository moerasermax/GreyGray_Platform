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
/// M2 本地批發進貨的存貨分錄，<b>以及它最容易踩到的雙重入帳陷阱</b>。
/// </summary>
/// <remarks>
/// <para>
/// 進貨成本的分錄本來只掛在 <c>procurement.GoodsReceived.v1</c> 上。M2 要讓批發進貨
/// 也入帳，直覺的做法是「加一個 <c>LotCreated</c> 的 handler」——但
/// <c>GoodsReceivedInventoryHandler</c> 建完批號之後<b>還會再發一次 <c>LotCreated</c></b>
/// （<c>Inventory.Infra/ModuleRegistration.cs</c>），所以那樣寫會讓代購那條線
/// 對同一批貨記兩次帳，存貨與現金雙雙翻倍。
/// </para>
/// <para>
/// <b>而且原本一條測試都不會紅</b>，因為在這一包之前沒有任何人訂閱 <c>LotCreated</c>。
/// 下面第二條就是專門守這件事的迴歸測試。
/// </para>
/// </remarks>
public sealed class LotCreatedLedgerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await ApplyMigrationsAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "本地批發進貨記 DR 存貨 / CR 現金，借貸相等且重送只入帳一次")]
    public async Task Local_wholesale_lot_posts_one_balanced_inventory_entry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        var lotId = LotId.New();
        var created = new LotCreated(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            lotId,
            SkuId.New(),
            LotSource.LocalWholesale,
            Money.OfMajor(180, Currency.TWD),
            12,
            FromCampaign: null);

        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider
                .GetServices<IIntegrationEventHandler<LotCreated>>()
                .ShouldNotBeEmpty(
                    "★ 沒有人訂閱 LotCreated——LotCreatedLedgerHandler 已經寫在 " +
                    "Ledger.Infra/LedgerEventHandlers.cs，但它的 DI 登錄要加在 " +
                    "Ledger.Infra/ModuleRegistration.cs，而那個檔不在 BE-38 的 allow 清單裡。" +
                    "詳見 .dispatch/reports/BE-38.md「我發現但沒做的事」。");
        }

        await DispatchAsync(provider, created, cancellationToken);
        await DispatchAsync(provider, created, cancellationToken);

        (await CountEntriesAsync("Inventory", lotId.ToString(), cancellationToken)).ShouldBe(1);
        (await DebitTotalAsync(AccountCodes.Inventory, cancellationToken)).ShouldBe(216_000);
        (await CreditTotalAsync(AccountCodes.Cash, cancellationToken)).ShouldBe(216_000);
        (await CountProcessedAsync(created.EventId, cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName =
        "★ 代購路徑：GoodsReceived 與它引發的 LotCreated 一起送，存貨分錄仍然只有一筆")]
    public async Task Overseas_purchase_path_posts_the_inventory_entry_exactly_once()
    {
        // 這一條是 docs/34 §1 的迴歸測試。正式環境的順序就是這樣：
        //   Procurement 發 GoodsReceived
        //     → Ledger 記 DR 存貨 / CR 現金
        //     → Inventory 建批號，接著發 LotCreated(Source = OverseasPurchase)
        //     → 派送器把 LotCreated 交給 Ledger
        // 第四步若不看 Source 就入帳，同一批貨會被記兩次。
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

        // 欄位逐一對齊 Inventory.Infra/ModuleRegistration.cs 的 GoodsReceivedInventoryHandler
        // 實際發出來的那一則——那才是正式環境會交到 Ledger 手上的東西。
        var lotId = LotId.New();
        var lotCreated = new LotCreated(
            Guid.CreateVersion7(),
            received.OccurredAt,
            received.TenantId,
            lotId,
            received.SkuId,
            received.Source,
            received.UnitCost,
            received.Quantity,
            received.CampaignId);

        await DispatchAsync(provider, received, cancellationToken);
        await DispatchAsync(provider, lotCreated, cancellationToken);

        // 一筆，不是兩筆。
        (await CountEntriesAsync("Procurement", received.EventId.ToString(), cancellationToken))
            .ShouldBe(1);
        (await CountEntriesAsync("Inventory", lotId.ToString(), cancellationToken))
            .ShouldBe(0, "代購那批已經由 GoodsReceivedLedgerHandler 記過，LotCreated 不能再記一次。");
        (await DebitTotalAsync(AccountCodes.Inventory, cancellationToken))
            .ShouldBe(175_000, "翻倍（350_000）就是雙重入帳。");
        (await CreditTotalAsync(AccountCodes.Cash, cancellationToken)).ShouldBe(175_000);
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
            typeof(LotCreated).Assembly,
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
        ScalarAsync<int>(
            "SELECT count(*)::integer FROM platform.processed_message WHERE event_id = @event_id;",
            cancellationToken,
            new NpgsqlParameter("event_id", eventId));

    private async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        // 上界從 db/migrations/ 的實際內容推導，不寫死（同 BE-37 的判準：
        // ops/ 的部署腳本該列死但要維護，測試該推導）。
        var migrationDirectory = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        var paths = Directory.GetFiles(migrationDirectory, "*.sql")
            .Where(path => int.TryParse(Path.GetFileName(path).AsSpan(0, 4), out _))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        paths.ShouldNotBeEmpty($"{migrationDirectory} 底下找不到任何 NNNN_*.sql。");
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
