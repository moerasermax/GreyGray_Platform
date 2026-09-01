using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Inventory.Core;
using GreyGray.Modules.Inventory.Infra;
using GreyGray.Modules.Procurement.Contracts;
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
/// M2 批發進貨（<c>POST /v1/lots</c>）的模組層。
/// <para>
/// <b>這是目前唯一能讓本地現貨進到庫存裡的路徑。</b>在這之前程式裡只有代購流程的
/// <c>GoodsReceived</c> 會建立批號，所以前台每個 SKU 的 <c>available</c> 都是 0、
/// 單品頁一律「已售完」——那正是使用者 2026-09-01 在真瀏覽器裡看到的現象。
/// </para>
/// </summary>
public sealed class WholesaleReceiptPostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly MutableClock _clock = new() { Now = Now };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await ApplyMigrationsAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName =
        "批發進貨：同一把 Idempotency-Key 重送只有一個批號，而且前台的 available 立刻從 0 變成 N")]
    public async Task Wholesale_receipt_is_idempotent_and_shows_up_as_available_stock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        var skuId = SkuId.New();

        // 進貨之前：前台看到的就是 0，所以單品頁顯示「已售完」。
        (await AvailableAsync(provider, skuId, cancellationToken)).ShouldBe(0);

        Result<Lot> first;
        Result<Lot> replay;
        await using (var scope = provider.CreateAsyncScope())
        {
            var receiving = scope.ServiceProvider.GetRequiredService<IInventoryReceiving>();
            first = await receiving.ReceiveWholesaleAsync(
                new WholesaleReceipt(skuId, 12, Money.OfMajor(180, Currency.TWD), "B-2026-09"),
                "wholesale-key-1",
                cancellationToken);
            replay = await receiving.ReceiveWholesaleAsync(
                new WholesaleReceipt(skuId, 12, Money.OfMajor(180, Currency.TWD), "B-2026-09"),
                "wholesale-key-1",
                cancellationToken);
        }

        first.IsSuccess.ShouldBeTrue();
        replay.IsSuccess.ShouldBeTrue();
        replay.Value.Id.ShouldBe(first.Value.Id);

        (await CountAsync("inventory.lot", cancellationToken)).ShouldBe(1);
        // 重播不能再發一次事件——否則 Ledger 會對同一批貨再記一次帳。
        (await CountOutboxAsync(LotCreated.EventType, cancellationToken)).ShouldBe(1);

        first.Value.Source.ShouldBe(LotSource.LocalWholesale);
        first.Value.FromCampaign.HasValue.ShouldBeFalse("本地批發沒有團。");
        first.Value.BatchCode.ShouldBe("B-2026-09");
        first.Value.UnitCost.ShouldBe(Money.OfMajor(180, Currency.TWD));
        first.Value.QuantityOnHand.ShouldBe(12);
        first.Value.QuantityAvailable.ShouldBe(12);
        first.Value.ReceivedAt.ShouldBe(Now, "時間必須經 IClock（六條鐵則第 2 條）。");

        // ★ 這一行就是「前台單品頁 available 從 0 變成 N」的根據：
        // 前台的數量選擇器讀的正是 GetAvailabilityAsync。
        (await AvailableAsync(provider, skuId, cancellationToken)).ShouldBe(12);

        // outbox 裡的 LotCreated 要帶得出 LocalWholesale，Ledger 那邊才分得出
        // 「這一批要入帳」與「代購那條線已經由 GoodsReceived 入過帳」。
        (await ScalarAsync<string>("""
            SELECT payload->>'source' FROM platform.outbox_message WHERE event_type = @event_type;
            """, cancellationToken, new NpgsqlParameter("event_type", LotCreated.EventType)))
            .ShouldBe("LocalWholesale");
    }

    [Fact(DisplayName =
        "★ 不同的 key、同一個 SKU、同樣數量與成本 → 建得出第二個批號（永久擋住「自然鍵」那個方向）")]
    public async Task A_different_key_creates_a_second_lot_for_the_same_sku_quantity_and_cost()
    {
        // 這一條守的是一個「全套測試會全綠、正式環境會壞掉」的修法方向：
        // 若把冪等做成 (tenant_id, sku_id, quantity, unit_cost) 這種自然鍵，
        // 「同一個 SKU 一週進三次一模一樣的貨」就會被當成重播吃掉——而那是日常，
        // 批號存在的理由正是「同一個 SKU 的多批貨各自帶自己的成本」。
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        var skuId = SkuId.New();
        var receipt = new WholesaleReceipt(skuId, 10, Money.OfMajor(180, Currency.TWD), null);

        Result<Lot> monday;
        Result<Lot> thursday;
        await using (var scope = provider.CreateAsyncScope())
        {
            var receiving = scope.ServiceProvider.GetRequiredService<IInventoryReceiving>();
            monday = await receiving.ReceiveWholesaleAsync(receipt, "monday", cancellationToken);
            thursday = await receiving.ReceiveWholesaleAsync(receipt, "thursday", cancellationToken);
        }

        monday.IsSuccess.ShouldBeTrue();
        thursday.IsSuccess.ShouldBeTrue();
        thursday.Value.Id.ShouldNotBe(monday.Value.Id);
        (await CountAsync("inventory.lot", cancellationToken)).ShouldBe(2);
        (await CountOutboxAsync(LotCreated.EventType, cancellationToken)).ShouldBe(2);
        (await AvailableAsync(provider, skuId, cancellationToken)).ShouldBe(20);
    }

    [Fact(DisplayName = "同一把 key 換了內容 → inventory.idempotency-key-reused，不建第二個批號")]
    public async Task Reusing_a_key_with_different_content_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        var skuId = SkuId.New();
        var otherSkuId = SkuId.New();
        await using (var scope = provider.CreateAsyncScope())
        {
            var receiving = scope.ServiceProvider.GetRequiredService<IInventoryReceiving>();
            (await receiving.ReceiveWholesaleAsync(
                new WholesaleReceipt(skuId, 10, Money.OfMajor(180, Currency.TWD), null),
                "reused",
                cancellationToken)).IsSuccess.ShouldBeTrue();

            var differentSku = await receiving.ReceiveWholesaleAsync(
                new WholesaleReceipt(otherSkuId, 10, Money.OfMajor(180, Currency.TWD), null),
                "reused",
                cancellationToken);
            differentSku.IsFailure.ShouldBeTrue();
            differentSku.Error.Code.ShouldBe("inventory.idempotency-key-reused");

            var differentCost = await receiving.ReceiveWholesaleAsync(
                new WholesaleReceipt(skuId, 10, Money.OfMajor(200, Currency.TWD), null),
                "reused",
                cancellationToken);
            differentCost.IsFailure.ShouldBeTrue();
            differentCost.Error.Code.ShouldBe("inventory.idempotency-key-reused");
        }

        (await CountAsync("inventory.lot", cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName =
        "沒有 Idempotency-Key（或超過 255 字元）建不出批號，code 逐字是 platform.idempotency-key-required")]
    public async Task Missing_idempotency_key_is_rejected_with_the_contract_code()
    {
        // 逐字比對：docs/05-API契約.md §4 規定這個碼是 platform. 前綴，
        // 而 #24 之所以能活到 BE-37 才被發現，就是因為沒有任何測試斷言過這些字串。
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        var receipt = new WholesaleReceipt(
            SkuId.New(),
            10,
            Money.OfMajor(180, Currency.TWD),
            null);
        await using (var scope = provider.CreateAsyncScope())
        {
            var receiving = scope.ServiceProvider.GetRequiredService<IInventoryReceiving>();

            var blank = await receiving.ReceiveWholesaleAsync(receipt, "   ", cancellationToken);
            blank.IsFailure.ShouldBeTrue();
            blank.Error.Code.ShouldBe("platform.idempotency-key-required");

            var tooLong = await receiving.ReceiveWholesaleAsync(
                receipt,
                new string('k', 256),
                cancellationToken);
            tooLong.IsFailure.ShouldBeTrue();
            tooLong.Error.Code.ShouldBe("platform.idempotency-key-required");
        }

        (await CountAsync("inventory.lot", cancellationToken)).ShouldBe(0);
    }

    [Fact(DisplayName = "批發進貨的數量、成本、幣別與批號代碼違規時回可預期失敗，不是例外")]
    public void Invalid_wholesale_input_is_rejected_as_a_result_failure()
    {
        var invalidQuantity = LotAggregate.CreateFromWholesale(
            LotId.New(), TenantId.Default, SkuId.New(), 0,
            Money.OfMajor(100, Currency.TWD), null, "key", Now);
        invalidQuantity.IsFailure.ShouldBeTrue();
        invalidQuantity.Error.Code.ShouldBe("inventory.invalid-wholesale-quantity");

        var negativeCost = LotAggregate.CreateFromWholesale(
            LotId.New(), TenantId.Default, SkuId.New(), 1,
            Money.OfMajor(-1, Currency.TWD), null, "key", Now);
        negativeCost.IsFailure.ShouldBeTrue();
        negativeCost.Error.Code.ShouldBe("inventory.invalid-wholesale-unit-cost");

        // 幣別走 new Money(...) 而不是 Money.OfMajor(...)：後者會先問 MinorUnitsPerUnit()，
        // 未知幣別在那裡就丟例外，測不到聚合自己的守衛。
        var unknownCurrency = LotAggregate.CreateFromWholesale(
            LotId.New(), TenantId.Default, SkuId.New(), 1,
            new Money(10_000, (Currency)999), null, "key", Now);
        unknownCurrency.IsFailure.ShouldBeTrue();
        unknownCurrency.Error.Code.ShouldBe("inventory.invalid-wholesale-unit-cost");

        var longBatchCode = LotAggregate.CreateFromWholesale(
            LotId.New(), TenantId.Default, SkuId.New(), 1,
            Money.OfMajor(100, Currency.TWD), new string('b', 65), "key", Now);
        longBatchCode.IsFailure.ShouldBeTrue();
        longBatchCode.Error.Code.ShouldBe("inventory.invalid-batch-code");
    }

    [Fact(DisplayName = "批號列表可依 SKU 過濾、游標分頁，回的是新到舊")]
    public async Task Lot_list_filters_by_sku_and_pages_with_a_cursor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        var skuId = SkuId.New();
        var otherSkuId = SkuId.New();
        var created = new List<LotId>();
        await using (var scope = provider.CreateAsyncScope())
        {
            var receiving = scope.ServiceProvider.GetRequiredService<IInventoryReceiving>();
            for (var index = 0; index < 3; index++)
            {
                // received_at 是游標的比較欄位，所以三批貨要落在不同的時間。
                _clock.Now = Now.AddMinutes(index);
                var lot = await receiving.ReceiveWholesaleAsync(
                    new WholesaleReceipt(skuId, 1 + index, Money.OfMajor(180, Currency.TWD), null),
                    $"page-{index}",
                    cancellationToken);
                lot.IsSuccess.ShouldBeTrue();
                created.Add(lot.Value.Id);
            }

            _clock.Now = Now.AddMinutes(10);
            (await receiving.ReceiveWholesaleAsync(
                new WholesaleReceipt(otherSkuId, 99, Money.OfMajor(180, Currency.TWD), null),
                "page-other",
                cancellationToken)).IsSuccess.ShouldBeTrue();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var lots = scope.ServiceProvider.GetRequiredService<IInventoryLotQuery>();

            var all = await lots.ListLotsAsync(
                new AdminLotListRequest(null, null, 20),
                cancellationToken);
            all.IsSuccess.ShouldBeTrue();
            all.Value.Items.Count.ShouldBe(4);
            all.Value.NextCursor.ShouldBeNull();

            var filtered = await lots.ListLotsAsync(
                new AdminLotListRequest(skuId, null, 20),
                cancellationToken);
            filtered.IsSuccess.ShouldBeTrue();
            var filteredIds = filtered.Value.Items.Select(lot => lot.Id).ToArray();
            filteredIds.Length.ShouldBe(3);
            filteredIds[0].ShouldBe(created[2], "新到舊。");
            filteredIds[1].ShouldBe(created[1]);
            filteredIds[2].ShouldBe(created[0]);

            var firstPage = await lots.ListLotsAsync(
                new AdminLotListRequest(skuId, null, 2),
                cancellationToken);
            firstPage.IsSuccess.ShouldBeTrue();
            var firstPageIds = firstPage.Value.Items.Select(lot => lot.Id).ToArray();
            firstPageIds.Length.ShouldBe(2);
            firstPageIds[0].ShouldBe(created[2]);
            firstPageIds[1].ShouldBe(created[1]);
            firstPage.Value.NextCursor.ShouldBe(created[1].ToString());

            var secondPage = await lots.ListLotsAsync(
                new AdminLotListRequest(skuId, created[1], 2),
                cancellationToken);
            secondPage.IsSuccess.ShouldBeTrue();
            var secondPageIds = secondPage.Value.Items.Select(lot => lot.Id).ToArray();
            secondPageIds.Length.ShouldBe(1);
            secondPageIds[0].ShouldBe(created[0]);
            secondPage.Value.NextCursor.ShouldBeNull();
        }
    }

    [Fact(DisplayName =
        "0017 可重跑，過濾式唯一索引真的存在、真的擋，而且不會擋到沒有鍵的既有資料列")]
    public async Task Migration_0017_creates_an_enforcing_filtered_unique_index()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        var migrationPath = Path.Combine(
            FindRepositoryRoot(),
            "db",
            "migrations",
            "0017_inventory_lot_wholesale_idempotency.sql");
        var sql = string.Join(
            Environment.NewLine,
            File.ReadLines(migrationPath).Where(line => !line.TrimStart().StartsWith('\\')));

        // 重跑一次：ops/invoke-migrations.ps1 沒有「已套用就跳過」的追蹤，重放是常態。
        await ExecuteAsync(sql, cancellationToken);

        (await ScalarAsync<string>("""
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'inventory' AND indexname = 'ux_lot_tenant_creation_key';
            """, cancellationToken)).ShouldContain("WHERE (creation_idempotency_key IS NOT NULL)");

        // 沒有鍵的既有資料列不能互相撞在一起——這正是索引要加 WHERE 的理由。
        await InsertRawLotAsync(null, cancellationToken);
        await InsertRawLotAsync(null, cancellationToken);
        (await CountAsync("inventory.lot", cancellationToken)).ShouldBe(2);

        await InsertRawLotAsync("dup", cancellationToken);
        var duplicate = await Should.ThrowAsync<PostgresException>(
            () => InsertRawLotAsync("dup", cancellationToken));
        duplicate.SqlState.ShouldBe("23505");
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
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<ICorrelationContext>(new CorrelationContext());
        services.AddSingleton(EventTypeRegistry.FromAssemblies([
            typeof(LotCreated).Assembly,
            typeof(GoodsReceived).Assembly,
        ]));
        services.AddInventoryModule(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task<int> AvailableAsync(
        ServiceProvider provider,
        SkuId skuId,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryQuery>();
        var availability = await inventory.GetAvailabilityAsync([skuId], cancellationToken);
        availability.IsSuccess.ShouldBeTrue();
        return availability.Value.Single().Available;
    }

    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        _clock.Now = Now;
        await ExecuteAsync("""
            TRUNCATE TABLE
                inventory.reservation_allocation,
                inventory.reservation,
                inventory.lot,
                platform.outbox_message,
                platform.processed_message
            CASCADE;
            """, cancellationToken);
    }

    private Task InsertRawLotAsync(string? idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO inventory.lot (
                id, tenant_id, sku_id, quantity_on_hand, unit_cost_amount_minor,
                unit_cost_currency, source, received_at, creation_idempotency_key)
            VALUES (
                gen_random_uuid(), @tenant_id, gen_random_uuid(), 1, 100,
                'TWD', 1, now(), @key);
            """, cancellationToken,
            new NpgsqlParameter("tenant_id", TenantId.Default.Value),
            new NpgsqlParameter("key", (object?)idempotencyKey ?? DBNull.Value));

    private Task<int> CountAsync(string table, CancellationToken cancellationToken) =>
        ScalarAsync<int>($"SELECT count(*)::integer FROM {table};", cancellationToken);

    private Task<int> CountOutboxAsync(string eventType, CancellationToken cancellationToken) =>
        ScalarAsync<int>(
            "SELECT count(*)::integer FROM platform.outbox_message WHERE event_type = @event_type;",
            cancellationToken,
            new NpgsqlParameter("event_type", eventType));

    private async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        // 上界從 db/migrations/ 的實際內容推導，不寫死：寫死的話每加一份 migration
        // 就悄悄少套一份，測試不會紅，只會測得比它宣稱的少——那比紅還難發現。
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

    /// <summary>可撥動的時鐘：分頁測試需要三批貨落在不同的 <c>received_at</c>。</summary>
    private sealed class MutableClock : IClock
    {
        public DateTimeOffset Now { get; set; }

        public DateTimeOffset UtcNow => Now;

        public DateOnly TodayInTaipei => DateOnly.FromDateTime(Now.UtcDateTime.AddHours(8));
    }
}
