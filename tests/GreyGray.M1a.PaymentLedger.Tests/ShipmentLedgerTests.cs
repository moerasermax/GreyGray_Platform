using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ledger.Contracts;
using GreyGray.Modules.Ledger.Infra;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
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
/// <c>docs/02-事件與狀態機.md</c> §5 分錄對照表<b>第 ⑦ 階段那兩筆</b>（#43）。
/// </summary>
/// <remarks>
/// <para>
/// 這兩筆從第一天就寫在分錄表裡，事件型別也一直都在（<c>StockCostAllocated</c> 的 XML doc
/// 明寫「Ledger 訂閱後開 DR 銷貨成本 / CR 存貨」、<c>ShipmentDispatched</c> 的明寫
/// 「Ledger 訂閱後開 DR 運費成本 / CR 現金」），<b>但一直沒有人訂閱</b>。
/// 沒有 handler 的事件會被 <c>OutboxDispatcher</c> 直接標成已處理，所以正式機上
/// 出貨那一整段一毛都沒落帳，而且<b>沒有任何一條測試會紅</b>。
/// </para>
/// <para>
/// 「有沒有人接」的機械把關在
/// <c>tests/GreyGray.Architecture.Tests/LedgerCoverageTests.cs</c>；
/// 這裡驗的是借貸科目、金額與冪等這些「有訂閱之後還可能記錯」的部分。
/// </para>
/// </remarks>
public sealed class ShipmentLedgerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await ApplyMigrationsAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "出貨結轉：StockCostAllocated 開 DR 銷貨成本 / CR 存貨，sourceRef 沿用不包一層")]
    public async Task Stock_cost_allocated_posts_cost_of_goods_sold_against_inventory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        // TotalCost 是「單位成本 × 數量」，由 Inventory 算好帶過來：20 元 × 5 件 = 100 元。
        var totalCost = Money.OfMajor(20, Currency.TWD).MultiplyByQuantity(5);
        totalCost.AmountMinor.ShouldBe(10_000, "Money.MultiplyByQuantity 才是唯一的乘法入口（鐵則 1）。");

        var orderId = OrderId.New();
        var skuId = SkuId.New();
        var allocated = new StockCostAllocated(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            LotId.New(),
            skuId,
            5,
            totalCost,
            $"{orderId}:{skuId}");

        await DispatchAsync(provider, allocated, cancellationToken);
        await DispatchAsync(provider, allocated, cancellationToken);

        (await CountEntriesAsync("Inventory", allocated.SourceRef, cancellationToken))
            .ShouldBe(1, "重放不能開第二筆分錄。");
        (await DebitTotalAsync(AccountCodes.CostOfGoodsSold, cancellationToken)).ShouldBe(10_000);
        (await CreditTotalAsync(AccountCodes.Inventory, cancellationToken)).ShouldBe(10_000);
        (await CountProcessedAsync(allocated.EventId, cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "交運運費：ShipmentDispatched 開 DR 運費成本 / CR 現金，source_ref 是出貨單")]
    public async Task Shipment_dispatched_posts_shipping_cost_against_cash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        var shipmentId = ShipmentId.New();
        var dispatched = new ShipmentDispatched(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            shipmentId,
            [OrderId.New(), OrderId.New()],
            DeliveryMethod.HomeDelivery,
            "TRACK-0001",
            Money.OfMajor(111, Currency.TWD));

        await DispatchAsync(provider, dispatched, cancellationToken);
        await DispatchAsync(provider, dispatched, cancellationToken);

        (await CountEntriesAsync("Fulfillment", shipmentId.ToString(), cancellationToken))
            .ShouldBe(1, "一張出貨單只有一筆運費成本，不管它合併了幾張訂單。");
        (await DebitTotalAsync(AccountCodes.ShippingCost, cancellationToken)).ShouldBe(11_100);
        (await CreditTotalAsync(AccountCodes.Cash, cancellationToken)).ShouldBe(11_100);
    }

    [Fact(DisplayName = "★ CarrierCost 為零時完全不開分錄——不是開一筆全零的 entry")]
    public async Task Zero_carrier_cost_posts_nothing_at_all()
    {
        // 全零的 entry 借貸當然相等，所以「借貸不變式」那條測試抓不到它；
        // 但帳上多一筆金額 0 的憑證，只會讓「這張出貨單到底有沒有運費成本」
        // 變成要點進去看才知道。
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        var shipmentId = ShipmentId.New();
        var dispatched = new ShipmentDispatched(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            shipmentId,
            [OrderId.New()],
            DeliveryMethod.ConvenienceStore,
            "TRACK-0002",
            Money.Zero(Currency.TWD));

        await DispatchAsync(provider, dispatched, cancellationToken);

        (await CountEntriesAsync("Fulfillment", shipmentId.ToString(), cancellationToken)).ShouldBe(0);
        (await CountAsync("ledger.journal_entry", cancellationToken)).ShouldBe(0);
        (await CountAsync("ledger.journal_line", cancellationToken)).ShouldBe(0);
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
            typeof(StockCostAllocated).Assembly,
            typeof(ShipmentDispatched).Assembly,
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
        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>().ToArray();
        handlers.ShouldNotBeEmpty(
            $"★ 沒有人訂閱 {typeof(TEvent).Name}——docs/02 §5 分錄表第 ⑦ 階段那一筆會整個消失。");
        foreach (var handler in handlers)
        {
            await handler.HandleAsync(@event, cancellationToken);
        }
    }

    private async Task ResetAsync(CancellationToken cancellationToken) =>
        await ExecuteAsync("""
            TRUNCATE TABLE
                ledger.journal_line,
                ledger.journal_entry,
                platform.outbox_message,
                platform.processed_message
            CASCADE;
            """, cancellationToken);

    private Task<int> CountAsync(string table, CancellationToken cancellationToken) =>
        ScalarAsync<int>($"SELECT count(*)::integer FROM {table};", cancellationToken);

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
        // 上界從 db/migrations/ 的實際內容推導，不寫死（同 LotCreatedLedgerTests）。
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
