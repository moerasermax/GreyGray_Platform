using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Fulfillment.Contracts;
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

/// <summary>
/// 交運出庫（#43）：貨離開倉庫時 <c>quantity_on_hand</c> 與 <c>quantity_reserved</c> 一起減，
/// 並逐筆發 <c>StockCostAllocated</c> 讓 Ledger 結轉銷貨成本。
/// </summary>
/// <remarks>
/// <para>
/// 2026-09-03 正式機的狀況：貨已經寄到客人手上，<c>inventory.lot</c> 還是
/// <c>on_hand=60 / reserved=5</c>，<c>reservation.status</c> 還是 0。
/// 原因是 Inventory 從來沒有任何 shipment 事件的 handler。
/// </para>
/// <para>
/// <b>冪等不能靠 <c>platform.processed_message</c></b>：它的去重範圍是 (event, handler)，
/// 而一張訂單可以拆進多張出貨單——第二張出貨單是<b>不同的事件</b>，框架擋不住。
/// 所以下面第二條測試送的是兩個 EventId 不同、OrderIds 相同的 <c>ShipmentDispatched</c>。
/// </para>
/// </remarks>
public sealed class ShipmentDispatchInventoryTests : IAsyncLifetime
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

    [Fact(DisplayName =
        "交運出庫：on_hand 與 reserved 都減、狀態轉已出庫，第二張出貨單不會再扣一次")]
    public async Task Dispatch_consumes_stock_once_even_when_a_second_shipment_repeats_the_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        var skuId = SkuId.New();
        var lotId = await InsertLotAsync(skuId, quantity: 60, unitCostMinor: 2_000, cancellationToken);
        var orderId = OrderId.New();
        await ReserveAsync(provider, orderId, skuId, 5, cancellationToken);

        (await OnHandAsync(lotId, cancellationToken)).ShouldBe(60);
        (await ReservedAsync(lotId, cancellationToken)).ShouldBe(5);

        var dispatched = Dispatch([orderId]);
        await DispatchAsync(provider, dispatched, cancellationToken);
        // 同一個事件重放：框架的 processed_message 會擋。
        await DispatchAsync(provider, dispatched, cancellationToken);
        // ★ 第二張出貨單，EventId 不同、orderId 相同：框架擋不到，只有 reservation 狀態擋得住。
        await DispatchAsync(provider, Dispatch([orderId]), cancellationToken);

        (await OnHandAsync(lotId, cancellationToken)).ShouldBe(55, "60 - 5，只能扣一次。");
        (await ReservedAsync(lotId, cancellationToken)).ShouldBe(0);
        (await AvailableAsync(lotId, cancellationToken)).ShouldBe(
            55,
            "quantity_available 是 generated column，由資料庫自己算出 55 - 0 - 0。");
        (await ReservationStatusAsync(orderId, cancellationToken)).ShouldBe((short)2);
        (await ConsumedAtIsNullAsync(orderId, cancellationToken)).ShouldBeFalse();
        (await ReleasedAtIsNullAsync(orderId, cancellationToken)).ShouldBeTrue(
            "出庫不是釋放，released_at 必須維持 NULL（0018 的 constraint 也這樣要求）。");
        (await CountOutboxAsync(StockCostAllocated.EventType, cancellationToken))
            .ShouldBe(1, "重放與第二張出貨單都不能再發一次結轉事件。");
    }

    [Fact(DisplayName =
        "★ 同一個 SKU 跨兩個批號：兩筆 StockCostAllocated 的成本各算各的，SourceRef 帶批號才不會撞")]
    public async Task Allocations_across_two_lots_emit_two_events_with_distinct_source_refs()
    {
        // Ledger 是用 (source_module, source_ref) 去重的（LedgerPostingService.PostAsync），
        // 所以 SourceRef 少了 lotId，第二個批號那一筆會被靜靜吃掉、銷貨成本少認一段。
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        var skuId = SkuId.New();
        var cheapLot = await InsertLotAsync(skuId, quantity: 3, unitCostMinor: 2_000, cancellationToken);
        var pricyLot = await InsertLotAsync(skuId, quantity: 4, unitCostMinor: 5_000, cancellationToken);
        var orderId = OrderId.New();
        await ReserveAsync(provider, orderId, skuId, 5, cancellationToken);

        await DispatchAsync(provider, Dispatch([orderId]), cancellationToken);

        // 兩個批號加起來剛好 5 件，兩個都被吃掉一部分。
        (await OnHandAsync(cheapLot, cancellationToken) + await OnHandAsync(pricyLot, cancellationToken))
            .ShouldBe(2, "3 + 4 - 5 = 2。");
        (await ReservedAsync(cheapLot, cancellationToken)).ShouldBe(0);
        (await ReservedAsync(pricyLot, cancellationToken)).ShouldBe(0);

        var payloads = await OutboxPayloadsAsync(StockCostAllocated.EventType, cancellationToken);
        payloads.Length.ShouldBe(2, "一個 allocation 一筆，不是整條 line 一筆。");
        payloads.ShouldAllBe(payload => payload.Contains($"{orderId}:{skuId}:", StringComparison.Ordinal));
        payloads.Count(payload => payload.Contains(cheapLot.ToString("N"), StringComparison.Ordinal))
            .ShouldBe(1);
        payloads.Count(payload => payload.Contains(pricyLot.ToString("N"), StringComparison.Ordinal))
            .ShouldBe(1);

        // TotalCost = 單位成本 × 數量：便宜那批 3 件 × 2,000 = 6,000；貴那批 2 件 × 5,000 = 10,000。
        payloads.Count(payload => payload.Contains("\"amountMinor\": 6000", StringComparison.Ordinal))
            .ShouldBe(1, $"實際 payload：{string.Join(" | ", payloads)}");
        payloads.Count(payload => payload.Contains("\"amountMinor\": 10000", StringComparison.Ordinal))
            .ShouldBe(1, $"實際 payload：{string.Join(" | ", payloads)}");
    }

    [Fact(DisplayName = "純預購訂單沒有現貨保留，交運安靜跳過，不是錯誤")]
    public async Task Dispatch_of_a_preorder_only_order_is_silently_skipped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        // 完全沒有 reservation：預購 line 不保留現貨（OrderPlacedInventoryHandler 只挑 Stock）。
        await DispatchAsync(provider, Dispatch([OrderId.New()]), cancellationToken);

        (await CountAsync("inventory.reservation", cancellationToken)).ShouldBe(0);
        (await CountOutboxAsync(StockCostAllocated.EventType, cancellationToken)).ShouldBe(0);
    }

    [Fact(DisplayName = "★ 訂單已取消（保留已釋放）卻又交運：失敗並講清楚，不默默吞掉")]
    public async Task Dispatch_after_release_fails_loudly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        var skuId = SkuId.New();
        var lotId = await InsertLotAsync(skuId, quantity: 10, unitCostMinor: 2_000, cancellationToken);
        var orderId = OrderId.New();
        var reservationId = await ReserveAsync(provider, orderId, skuId, 4, cancellationToken);

        await using (var scope = provider.CreateAsyncScope())
        {
            var released = await scope.ServiceProvider.GetRequiredService<IStockReservation>()
                .ReleaseAsync(reservationId, cancellationToken);
            released.IsSuccess.ShouldBeTrue();
        }

        Result consume;
        await using (var scope = provider.CreateAsyncScope())
        {
            consume = await scope.ServiceProvider.GetRequiredService<IStockReservation>()
                .ConsumeAsync($"ordering:{orderId}", orderId.ToString(), cancellationToken);
        }

        consume.IsFailure.ShouldBeTrue("已釋放的保留再出庫是帳實不符，不能安靜跳過。");
        consume.Error.Code.ShouldBe("inventory.reservation-already-released");
        (await OnHandAsync(lotId, cancellationToken)).ShouldBe(10, "失敗不能扣到庫存。");
        (await CountOutboxAsync(StockCostAllocated.EventType, cancellationToken)).ShouldBe(0);
    }

    [Fact(DisplayName =
        "★ 批號沒有單位成本：整筆出庫失敗，不會「扣了庫存卻沒結轉成本」")]
    public async Task Dispatch_fails_when_the_lot_has_no_unit_cost()
    {
        // M1a 既有的 STOCK 批號沒有 unit_cost（0010 才加的欄位，可為 NULL）。
        // 派工書沒有講這種批號怎麼辦。選 fail-closed：扣了庫存卻不發 StockCostAllocated，
        // 就是「銷貨成本安靜地少一段」——正是 #43 本身的形狀。詳見 .dispatch/reports/BE-47.md。
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        await using var provider = BuildProvider();

        var skuId = SkuId.New();
        var lotId = await InsertLotAsync(skuId, quantity: 10, unitCostMinor: null, cancellationToken);
        var orderId = OrderId.New();
        await ReserveAsync(provider, orderId, skuId, 4, cancellationToken);

        Result consume;
        await using (var scope = provider.CreateAsyncScope())
        {
            consume = await scope.ServiceProvider.GetRequiredService<IStockReservation>()
                .ConsumeAsync($"ordering:{orderId}", orderId.ToString(), cancellationToken);
        }

        consume.IsFailure.ShouldBeTrue();
        consume.Error.Code.ShouldBe("inventory.lot-unit-cost-missing");
        (await OnHandAsync(lotId, cancellationToken)).ShouldBe(10);
        (await ReservedAsync(lotId, cancellationToken)).ShouldBe(4, "保留維持原狀，等人補成本再出。");
        (await ReservationStatusAsync(orderId, cancellationToken)).ShouldBe((short)0);
    }

    private static ShipmentDispatched Dispatch(IReadOnlyList<OrderId> orderIds) =>
        new(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            ShipmentId.New(),
            orderIds,
            DeliveryMethod.HomeDelivery,
            "TRACK-0001",
            Money.OfMajor(111, Currency.TWD));

    private static async Task<ReservationId> ReserveAsync(
        ServiceProvider provider,
        OrderId orderId,
        SkuId skuId,
        int quantity,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var reserved = await scope.ServiceProvider.GetRequiredService<IStockReservation>()
            .ReserveAsync($"ordering:{orderId}", [(skuId, quantity)], cancellationToken);
        reserved.IsSuccess.ShouldBeTrue(
            reserved.IsFailure ? $"{reserved.Error.Code} {reserved.Error.Message}" : string.Empty);
        return reserved.Value;
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
            typeof(StockReserved).Assembly,
            typeof(ShipmentDispatched).Assembly,
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
        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>().ToArray();
        handlers.ShouldNotBeEmpty(
            $"★ Inventory 沒有訂閱 {typeof(TEvent).Name}——貨會寄出去，帳面庫存卻一件都不會少。");
        foreach (var handler in handlers)
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

    /// <summary>
    /// 插一個批號。<paramref name="unitCostMinor"/> 給 <c>null</c> 就是 M1a 既有那種
    /// 沒有成本的 STOCK 批號。
    /// <c>lot_receipt_fields_consistent</c>（0010）要求 unit_cost／currency／source／received_at
    /// <b>四個一起有或一起沒有</b>，所以這裡是整組切換，不是只切成本欄位。
    /// </summary>
    private async Task<Guid> InsertLotAsync(
        SkuId skuId,
        int quantity,
        long? unitCostMinor,
        CancellationToken cancellationToken)
    {
        var lotId = Guid.CreateVersion7();
        await ExecuteAsync("""
            INSERT INTO inventory.lot (
                id, tenant_id, sku_id, quantity_on_hand, quantity_reserved,
                unit_cost_amount_minor, unit_cost_currency, source, received_at)
            VALUES (
                @id, @tenant_id, @sku_id, @quantity, 0,
                @unit_cost, @currency, @source, @received_at);
            """, cancellationToken,
            new NpgsqlParameter("id", lotId),
            new NpgsqlParameter("tenant_id", TenantId.Default.Value),
            new NpgsqlParameter("sku_id", skuId.Value),
            new NpgsqlParameter("quantity", quantity),
            new NpgsqlParameter("unit_cost", unitCostMinor.HasValue ? unitCostMinor.Value : DBNull.Value),
            new NpgsqlParameter("currency", unitCostMinor.HasValue ? "TWD" : (object)DBNull.Value),
            new NpgsqlParameter("source", unitCostMinor.HasValue ? (short)1 : (object)DBNull.Value),
            new NpgsqlParameter("received_at", unitCostMinor.HasValue ? Now : (object)DBNull.Value));
        return lotId;
    }

    private Task<int> OnHandAsync(Guid lotId, CancellationToken cancellationToken) =>
        LotColumnAsync<int>("quantity_on_hand", lotId, cancellationToken);

    private Task<int> ReservedAsync(Guid lotId, CancellationToken cancellationToken) =>
        LotColumnAsync<int>("quantity_reserved", lotId, cancellationToken);

    private Task<int> AvailableAsync(Guid lotId, CancellationToken cancellationToken) =>
        LotColumnAsync<int>("quantity_available", lotId, cancellationToken);

    private Task<T> LotColumnAsync<T>(
        string column,
        Guid lotId,
        CancellationToken cancellationToken) =>
        ScalarAsync<T>(
            $"SELECT {column} FROM inventory.lot WHERE id = @id;",
            cancellationToken,
            new NpgsqlParameter("id", lotId));

    private Task<int> CountAsync(string table, CancellationToken cancellationToken) =>
        ScalarAsync<int>($"SELECT count(*)::integer FROM {table};", cancellationToken);

    private Task<int> CountOutboxAsync(string eventType, CancellationToken cancellationToken) =>
        ScalarAsync<int>(
            "SELECT count(*)::integer FROM platform.outbox_message WHERE event_type = @event_type;",
            cancellationToken,
            new NpgsqlParameter("event_type", eventType));

    private async Task<string[]> OutboxPayloadsAsync(
        string eventType,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT payload::text FROM platform.outbox_message WHERE event_type = @event_type;",
            connection);
        command.Parameters.AddWithValue("event_type", eventType);
        var payloads = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            payloads.Add(reader.GetString(0));
        }

        return payloads.ToArray();
    }

    private Task<short> ReservationStatusAsync(OrderId orderId, CancellationToken cancellationToken) =>
        ScalarAsync<short>("""
            SELECT status
            FROM inventory.reservation
            WHERE tenant_id = @tenant_id AND reservation_key = @reservation_key;
            """, cancellationToken,
            new NpgsqlParameter("tenant_id", TenantId.Default.Value),
            new NpgsqlParameter("reservation_key", $"ordering:{orderId}"));

    private Task<bool> ConsumedAtIsNullAsync(OrderId orderId, CancellationToken cancellationToken) =>
        TimestampIsNullAsync("consumed_at", orderId, cancellationToken);

    private Task<bool> ReleasedAtIsNullAsync(OrderId orderId, CancellationToken cancellationToken) =>
        TimestampIsNullAsync("released_at", orderId, cancellationToken);

    private Task<bool> TimestampIsNullAsync(
        string column,
        OrderId orderId,
        CancellationToken cancellationToken) =>
        ScalarAsync<bool>($"""
            SELECT {column} IS NULL
            FROM inventory.reservation
            WHERE tenant_id = @tenant_id AND reservation_key = @reservation_key;
            """, cancellationToken,
            new NpgsqlParameter("tenant_id", TenantId.Default.Value),
            new NpgsqlParameter("reservation_key", $"ordering:{orderId}"));

    private async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        // 上界從 db/migrations/ 的實際內容推導，不寫死——0018 也在裡面，
        // 而「每次部署把 0001 到最新全部重跑」正是這份 migration 必須冪等的理由。
        var migrationDirectory = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        var paths = Directory.GetFiles(migrationDirectory, "*.sql")
            .Where(path => int.TryParse(Path.GetFileName(path).AsSpan(0, 4), out _))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        paths.ShouldNotBeEmpty($"{migrationDirectory} 底下找不到任何 NNNN_*.sql。");
        foreach (var path in paths)
        {
            await ExecuteAsync(ReadMigration(path), cancellationToken);
        }

        // ★ 冪等對照：部署腳本每次都重跑全部，所以再跑一次 0018 不能炸。
        var eighteen = paths.Single(path =>
            Path.GetFileName(path).StartsWith("0018_", StringComparison.Ordinal));
        await ExecuteAsync(ReadMigration(eighteen), cancellationToken);
    }

    private static string ReadMigration(string path) =>
        string.Join(
            Environment.NewLine,
            File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('\\')));

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
