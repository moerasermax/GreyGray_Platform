using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Fulfillment.Core;
using GreyGray.Modules.Fulfillment.Infra;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1b.Fulfillment.Tests;

public sealed class FulfillmentPostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 3, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "交運：業務資料與 outbox 同交易，重送冪等；強制 outbox 失敗時業務欄位一併回滾")]
    public async Task Dispatch_is_idempotent_and_atomic_with_outbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<FulfillmentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        ShipmentId firstId;
        ShipmentId rollbackId;
        await using (var dbContext = new FulfillmentDbContext(options))
        {
            var first = ShipmentAggregate.Create(
                ShipmentId.New(), TenantId.Default, DeliveryMethod.HomeDelivery, [OrderId.New()],
                "dispatch-seed-first", Now).Value;
            var rollback = ShipmentAggregate.Create(
                ShipmentId.New(), TenantId.Default, DeliveryMethod.HomeDelivery, [OrderId.New()],
                "dispatch-seed-rollback", Now).Value;
            firstId = first.Id;
            rollbackId = rollback.Id;
            dbContext.Shipments.AddRange(first, rollback);
            await dbContext.SaveChangesAsync(cancellationToken);

            var service = new FulfillmentApplicationService(
                new FulfillmentRepository(dbContext),
                dbContext,
                new OutboxEventPublisher<FulfillmentDbContext>(
                    dbContext,
                    new MutableCorrelation { TenantId = TenantId.Default },
                    EventTypeRegistry.FromAssemblies([typeof(ShipmentDispatched).Assembly])),
                new FakeOrderQuery(),
                new FixedClock(Now),
                new MutableCorrelation { TenantId = TenantId.Default });

            var carrierCost = Money.OfMajor(150, Currency.TWD);
            (await service.DispatchAsync(firstId, "TRK-001", carrierCost, cancellationToken))
                .IsSuccess.ShouldBeTrue();
            (await service.DispatchAsync(firstId, "TRK-001", carrierCost, cancellationToken))
                .IsSuccess.ShouldBeTrue();

            (await dbContext.Set<OutboxMessage>().CountAsync(
                message => message.EventType == ShipmentDispatched.EventType,
                cancellationToken)).ShouldBe(1);

            await InstallRejectingOutboxTriggerAsync(
                connectionString,
                ShipmentDispatched.EventType,
                cancellationToken);
            await Should.ThrowAsync<DbUpdateException>(() =>
                service.DispatchAsync(rollbackId, "TRK-002", carrierCost, cancellationToken));
        }

        await using var verification = new FulfillmentDbContext(options);
        var rolledBack = await verification.Shipments.AsNoTracking().SingleAsync(
            shipment => shipment.Id == rollbackId,
            cancellationToken);
        rolledBack.Status.ShouldBe(ShipmentStatus.Draft);
        rolledBack.TrackingNumber.ShouldBeNull();
        (await verification.Set<OutboxMessage>().CountAsync(
            message => message.EventType == ShipmentDispatched.EventType,
            cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "Order 與 Shipment 是 N:M：一張訂單拆兩個包裹、一個包裹含兩張訂單")]
    public async Task Orders_and_shipments_are_many_to_many()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<FulfillmentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var orderSplitAcrossShipments = OrderId.New();
        var orderA = OrderId.New();
        var orderB = OrderId.New();

        var orders = new FakeOrderQuery();
        orders.Seed(orderSplitAcrossShipments);
        orders.Seed(orderA);
        orders.Seed(orderB);

        await using var dbContext = new FulfillmentDbContext(options);
        var service = new FulfillmentApplicationService(
            new FulfillmentRepository(dbContext),
            dbContext,
            new OutboxEventPublisher<FulfillmentDbContext>(
                dbContext,
                new MutableCorrelation { TenantId = TenantId.Default },
                EventTypeRegistry.FromAssemblies([typeof(ShipmentDispatched).Assembly])),
            orders,
            new FixedClock(Now),
            new MutableCorrelation { TenantId = TenantId.Default });

        // 一張訂單拆兩個包裹。
        var package1 = await service.CreateAsync(
            [orderSplitAcrossShipments], DeliveryMethod.HomeDelivery, "nm-package-1", cancellationToken);
        var package2 = await service.CreateAsync(
            [orderSplitAcrossShipments], DeliveryMethod.ConvenienceStore, "nm-package-2", cancellationToken);
        package1.IsSuccess.ShouldBeTrue();
        package2.IsSuccess.ShouldBeTrue();
        package1.Value.Id.ShouldNotBe(package2.Value.Id);

        var byOrder = await service.GetByOrderAsync(orderSplitAcrossShipments, cancellationToken);
        byOrder.IsSuccess.ShouldBeTrue();
        byOrder.Value.Select(shipment => shipment.Id)
            .ShouldBe([package1.Value.Id, package2.Value.Id], ignoreOrder: true);

        // 一個包裹含同一位客人的兩張訂單。
        var merged = await service.CreateAsync(
            [orderA, orderB], DeliveryMethod.HomeDelivery, "nm-merged", cancellationToken);
        merged.IsSuccess.ShouldBeTrue();
        merged.Value.OrderIds.ShouldBe([orderA, orderB], ignoreOrder: true);

        var reloaded = await service.GetAsync(merged.Value.Id, cancellationToken);
        reloaded.IsSuccess.ShouldBeTrue();
        reloaded.Value.OrderIds.ShouldBe([orderA, orderB], ignoreOrder: true);
    }

    [Fact(DisplayName = "還沒待出貨的訂單不能建立出貨單")]
    public async Task Create_rejects_orders_not_ready_to_ship()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<FulfillmentDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var dbContext = new FulfillmentDbContext(options);
        var orders = new FakeOrderQuery();
        var orderId = OrderId.New();
        orders.Seed(orderId, OrderStatus.PaidAwaitingClose);
        var service = new FulfillmentApplicationService(
            new FulfillmentRepository(dbContext),
            dbContext,
            new OutboxEventPublisher<FulfillmentDbContext>(
                dbContext,
                new MutableCorrelation { TenantId = TenantId.Default },
                EventTypeRegistry.FromAssemblies([typeof(ShipmentDispatched).Assembly])),
            orders,
            new FixedClock(Now),
            new MutableCorrelation { TenantId = TenantId.Default });

        var result = await service.CreateAsync(
            [orderId], DeliveryMethod.HomeDelivery, "not-ready-to-ship", cancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("fulfillment.order-not-ready-to-ship");
    }

    [Fact(DisplayName =
        "#23：同一把 Idempotency-Key 建兩次只會有一張出貨單，回的是原本那一張")]
    public async Task Create_with_the_same_key_twice_yields_one_shipment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<FulfillmentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var orderA = OrderId.New();
        var orderB = OrderId.New();
        var orders = new FakeOrderQuery();
        orders.Seed(orderA);
        orders.Seed(orderB);

        await using var dbContext = new FulfillmentDbContext(options);
        var service = CreateService(dbContext, orders);

        var first = await service.CreateAsync(
            [orderA, orderB], DeliveryMethod.HomeDelivery, "dup-23", cancellationToken);
        var replay = await service.CreateAsync(
            [orderA, orderB], DeliveryMethod.HomeDelivery, "dup-23", cancellationToken);

        first.IsSuccess.ShouldBeTrue();
        replay.IsSuccess.ShouldBeTrue();
        replay.Value.Id.ShouldBe(first.Value.Id);
        replay.Value.OrderIds.ShouldBe([orderA, orderB], ignoreOrder: true);

        // 重播不能只是「回一樣的東西」——資料庫裡真的只能有一張，
        // 因為多出來的那一張會被撿貨、被交運，物流成本重複入帳。
        (await ScalarAsync<int>(connectionString,
            "SELECT count(*)::int FROM fulfillment.shipment;", cancellationToken))
            .ShouldBe(1);
        (await ScalarAsync<int>(connectionString,
            "SELECT count(*)::int FROM fulfillment.shipment_order;", cancellationToken))
            .ShouldBe(2);
    }

    [Fact(DisplayName =
        "N:M 沒被破壞：同一批訂單、同一個配送方式，換一把 key 照樣建得出第二張")]
    public async Task Same_orders_and_method_with_a_new_key_create_a_second_shipment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<FulfillmentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        // 既有的 Orders_and_shipments_are_many_to_many 拆包裹時用了兩個**不同**的
        // DeliveryMethod，蓋不到「同一個宅配拆兩箱」——那正是自然鍵修法會擋掉、
        // 而全套測試仍然全綠的那個回歸。這一條就是為了擋住它。
        var orderId = OrderId.New();
        var orders = new FakeOrderQuery();
        orders.Seed(orderId);

        await using var dbContext = new FulfillmentDbContext(options);
        var service = CreateService(dbContext, orders);

        var box1 = await service.CreateAsync(
            [orderId], DeliveryMethod.HomeDelivery, "split-box-1", cancellationToken);
        var box2 = await service.CreateAsync(
            [orderId], DeliveryMethod.HomeDelivery, "split-box-2", cancellationToken);

        box1.IsSuccess.ShouldBeTrue();
        box2.IsSuccess.ShouldBeTrue();
        box1.Value.Id.ShouldNotBe(box2.Value.Id);
        box1.Value.Method.ShouldBe(box2.Value.Method);
        (await ScalarAsync<int>(connectionString,
            "SELECT count(*)::int FROM fulfillment.shipment;", cancellationToken))
            .ShouldBe(2);
    }

    [Fact(DisplayName =
        "★ 重播排在訂單狀態守衛之前：出貨後訂單已離開 ReadyToShip，同一把 key 仍回原本那一張")]
    public async Task Replay_still_succeeds_after_the_order_left_ready_to_ship()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<FulfillmentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var orderId = OrderId.New();
        var orders = new FakeOrderQuery();
        orders.Seed(orderId);

        await using var dbContext = new FulfillmentDbContext(options);
        var service = CreateService(dbContext, orders);

        var created = await service.CreateAsync(
            [orderId], DeliveryMethod.HomeDelivery, "replay-after-dispatch", cancellationToken);
        created.IsSuccess.ShouldBeTrue();

        // 下游把訂單推走（ShipmentDispatched → Ordering 轉 Shipped）。
        // 查冪等鍵如果排在守衛後面，重播會被 fulfillment.order-not-ready-to-ship
        // 擋成失敗，變成「重試永遠回不了成功」的死路（#22 (A″) 同型）。
        orders.Seed(orderId, OrderStatus.Shipped);

        var replay = await service.CreateAsync(
            [orderId], DeliveryMethod.HomeDelivery, "replay-after-dispatch", cancellationToken);

        replay.IsSuccess.ShouldBeTrue();
        replay.Value.Id.ShouldBe(created.Value.Id);
    }

    [Fact(DisplayName = "同一把 key 換一批訂單要回 fulfillment.idempotency-key-reused")]
    public async Task Create_rejects_a_reused_key_with_different_orders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<FulfillmentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var orderA = OrderId.New();
        var orderB = OrderId.New();
        var orders = new FakeOrderQuery();
        orders.Seed(orderA);
        orders.Seed(orderB);

        await using var dbContext = new FulfillmentDbContext(options);
        var service = CreateService(dbContext, orders);

        (await service.CreateAsync([orderA], DeliveryMethod.HomeDelivery, "reused", cancellationToken))
            .IsSuccess.ShouldBeTrue();

        var differentOrders = await service.CreateAsync(
            [orderB], DeliveryMethod.HomeDelivery, "reused", cancellationToken);
        differentOrders.IsFailure.ShouldBeTrue();
        differentOrders.Error.Code.ShouldBe("fulfillment.idempotency-key-reused");

        // 配送方式不同也一樣不算重播。
        var differentMethod = await service.CreateAsync(
            [orderA], DeliveryMethod.ConvenienceStore, "reused", cancellationToken);
        differentMethod.IsFailure.ShouldBeTrue();
        differentMethod.Error.Code.ShouldBe("fulfillment.idempotency-key-reused");

        (await ScalarAsync<int>(connectionString,
            "SELECT count(*)::int FROM fulfillment.shipment;", cancellationToken))
            .ShouldBe(1);
    }

    [Fact(DisplayName = "沒有 Idempotency-Key 建不出出貨單")]
    public async Task Create_rejects_a_blank_idempotency_key()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<FulfillmentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var orderId = OrderId.New();
        var orders = new FakeOrderQuery();
        orders.Seed(orderId);

        await using var dbContext = new FulfillmentDbContext(options);
        var service = CreateService(dbContext, orders);

        var blank = await service.CreateAsync(
            [orderId], DeliveryMethod.HomeDelivery, "   ", cancellationToken);
        blank.IsFailure.ShouldBeTrue();
        blank.Error.Code.ShouldBe("platform.idempotency-key-required");

        var tooLong = await service.CreateAsync(
            [orderId], DeliveryMethod.HomeDelivery, new string('k', 256), cancellationToken);
        tooLong.IsFailure.ShouldBeTrue();
        tooLong.Error.Code.ShouldBe("platform.idempotency-key-required");

        (await ScalarAsync<int>(connectionString,
            "SELECT count(*)::int FROM fulfillment.shipment;", cancellationToken))
            .ShouldBe(0);
    }

    [Fact(DisplayName =
        "0016 可重跑，過濾式唯一索引真的存在、真的擋，而且不會擋到沒有鍵的既有資料列")]
    public async Task Migration_0016_creates_an_enforcing_filtered_unique_index()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(cancellationToken);
        var migrationPath = Path.Combine(
            FindRepositoryRoot(), "db", "migrations", "0016_fulfillment_shipment_idempotency.sql");

        // 第二次執行驗證可重跑性（CreateMigratedDatabaseAsync 已經套過一次）。
        await ExecuteScriptAsync(connectionString, migrationPath, cancellationToken);

        (await ScalarAsync<string>(connectionString, """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'fulfillment'
              AND tablename = 'shipment'
              AND indexname = 'ux_shipment_tenant_creation_key';
            """, cancellationToken))
            .ShouldContain("creation_idempotency_key IS NOT NULL");

        // 索引存在還不夠，要證明它真的擋得住。
        await ExecuteSqlAsync(connectionString, InsertShipmentSql(Guid.CreateVersion7(), "'k'"), cancellationToken);
        await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync(
            connectionString,
            InsertShipmentSql(Guid.CreateVersion7(), "'k'"),
            cancellationToken));

        // 沒有鍵的既有資料列不受影響——這正是索引要帶 WHERE 的理由。
        await ExecuteSqlAsync(connectionString, InsertShipmentSql(Guid.CreateVersion7(), "NULL"), cancellationToken);
        await ExecuteSqlAsync(connectionString, InsertShipmentSql(Guid.CreateVersion7(), "NULL"), cancellationToken);
        (await ScalarAsync<int>(connectionString,
            "SELECT count(*)::int FROM fulfillment.shipment WHERE creation_idempotency_key IS NULL;",
            cancellationToken))
            .ShouldBe(2);
    }

    private static string InsertShipmentSql(Guid id, string keyLiteral) => $"""
        INSERT INTO fulfillment.shipment (id, method, status, created_at, creation_idempotency_key)
        VALUES ('{id}'::uuid, 2, 0, now(), {keyLiteral});
        """;

    private static FulfillmentApplicationService CreateService(
        FulfillmentDbContext dbContext,
        FakeOrderQuery orders) =>
        new(
            new FulfillmentRepository(dbContext),
            dbContext,
            new OutboxEventPublisher<FulfillmentDbContext>(
                dbContext,
                new MutableCorrelation { TenantId = TenantId.Default },
                EventTypeRegistry.FromAssemblies([typeof(ShipmentDispatched).Assembly])),
            orders,
            new FixedClock(Now),
            new MutableCorrelation { TenantId = TenantId.Default });

    [Fact(DisplayName = "0012 可重跑，owner 與 tenant composite FK 完整")]
    public async Task Migration_is_idempotent_and_owned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(cancellationToken);
        var migrationPath = Path.Combine(FindRepositoryRoot(), "db", "migrations", "0012_m1b_fulfillment.sql");

        // 第二次執行驗證可重跑性。
        await ExecuteScriptAsync(connectionString, migrationPath, cancellationToken);

        (await ScalarAsync<string>(connectionString, """
            SELECT tableowner FROM pg_tables
            WHERE schemaname = 'fulfillment' AND tablename = 'shipment';
            """, cancellationToken)).ShouldBe("greygray_owner");
        (await ScalarAsync<int>(connectionString, """
            SELECT count(*)::int FROM pg_constraint
            WHERE conrelid = 'fulfillment.shipment_order'::regclass
              AND conname = 'shipment_order_shipment_same_tenant_fk';
            """, cancellationToken)).ShouldBe(1);
    }

    private async Task<string> CreateMigratedDatabaseAsync(CancellationToken cancellationToken)
    {
        var databaseName = $"m1b4_fulfillment_{Guid.NewGuid():N}";
        await ExecuteSqlAsync(
            _postgres.GetConnectionString(),
            $"CREATE DATABASE \"{databaseName}\";",
            cancellationToken);
        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName,
        };
        var connectionString = builder.ConnectionString;

        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        await ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0001_schemas_and_roles.sql"),
            cancellationToken);
        await ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0002_platform.sql"),
            cancellationToken);
        await ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0012_m1b_fulfillment.sql"),
            cancellationToken);
        await ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0016_fulfillment_shipment_idempotency.sql"),
            cancellationToken);
        return connectionString;
    }

    private static async Task InstallRejectingOutboxTriggerAsync(
        string connectionString,
        string eventType,
        CancellationToken cancellationToken) =>
        await ExecuteSqlAsync(connectionString, $"""
            CREATE OR REPLACE FUNCTION platform.reject_m1b4_outbox()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.event_type = '{eventType}' THEN
                    RAISE EXCEPTION 'forced M1b-4 rollback';
                END IF;
                RETURN NEW;
            END
            $$;
            DROP TRIGGER IF EXISTS reject_m1b4_outbox ON platform.outbox_message;
            CREATE TRIGGER reject_m1b4_outbox
                BEFORE INSERT ON platform.outbox_message
                FOR EACH ROW EXECUTE FUNCTION platform.reject_m1b4_outbox();
            """, cancellationToken);

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

    private static async Task ExecuteScriptAsync(
        string connectionString,
        string path,
        CancellationToken cancellationToken)
    {
        var sql = string.Join(
            Environment.NewLine,
            File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('\\')));
        await ExecuteSqlAsync(connectionString, sql, cancellationToken);
    }

    private static async Task ExecuteSqlAsync(
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
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return (T)(value ?? throw new InvalidOperationException("Expected a scalar value."));
    }
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;

    public DateOnly TodayInTaipei => DateOnly.FromDateTime(now.AddHours(8).DateTime);
}

internal sealed class MutableCorrelation : ICorrelationContext
{
    public string CorrelationId => "m1b-fulfillment-test";

    public string? CausationId => null;

    public TenantId TenantId { get; set; }
}

internal sealed class FakeOrderQuery : IOrderQuery
{
    private readonly Dictionary<OrderId, OrderView> _orders = new();

    public void Seed(OrderId id, OrderStatus status = OrderStatus.ReadyToShip)
    {
        _orders[id] = new OrderView(
            id,
            CustomerId.New(),
            SourceChannel.Own,
            status,
            ShippingPolicy.HoldUntilComplete,
            PricingSnapshotId.New(),
            Money.OfMajor(1000, Currency.TWD),
            Money.OfMajor(60, Currency.TWD),
            Money.OfMajor(1060, Currency.TWD),
            [],
            new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero))
        {
            OrderNumber = $"GG{id.Value:N}"[..15],
        };
    }

    public Task<Result<OrderView>> GetAsync(OrderId id, CancellationToken cancellationToken) =>
        Task.FromResult(_orders.TryGetValue(id, out var order)
            ? Result<OrderView>.Success(order)
            : Result<OrderView>.Failure("ordering.order-not-found", "找不到指定的訂單。"));

    public Task<Result<IReadOnlyList<OrderView>>> GetByCampaignAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Fulfillment 測試不需要依開團查詢訂單。");
}
