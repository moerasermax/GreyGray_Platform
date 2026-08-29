using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Campaign.Core;
using GreyGray.Modules.Campaign.Infra;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Modules.Procurement.Core;
using GreyGray.Modules.Procurement.Infra;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.M1b.Procurement.Tests;

public sealed class M1b3SeamsPostgresTests : IAsyncLifetime
{
    private const string TenantA = "10000000-0000-0000-0000-000000000001";
    private const string TenantB = "20000000-0000-0000-0000-000000000002";

    private static readonly DateTimeOffset Now =
        new(2026, 8, 29, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "GoodsReceived：業務資料與 outbox 同交易，command 重送只發一次")]
    public async Task Procurement_receipt_is_idempotent_and_atomic_with_outbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync("procurement", cancellationToken);
        var options = new DbContextOptionsBuilder<ProcurementDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var paid = new MoneyPair(
            Money.OfMajor(1_100, Currency.JPY),
            Money.OfMajor(250, Currency.TWD),
            null);

        PurchaseItemId firstId;
        PurchaseItemId rollbackId;
        await using (var dbContext = new ProcurementDbContext(options))
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            var first = CreatePurchasedItem(paid);
            var rollback = CreatePurchasedItem(paid);
            firstId = first.Id;
            rollbackId = rollback.Id;
            dbContext.PurchaseItems.AddRange(first, rollback);
            await dbContext.SaveChangesAsync(cancellationToken);

            var service = new ProcurementApplicationService(
                new ProcurementRepository(dbContext),
                dbContext,
                new OutboxEventPublisher<ProcurementDbContext>(
                    dbContext,
                    new MutableCorrelation { TenantId = TenantId.Default },
                    EventTypeRegistry.FromAssemblies([typeof(GoodsReceived).Assembly])),
                null!,
                null!,
                new FixedClock(Now),
                new MutableCorrelation { TenantId = TenantId.Default });

            (await service.MarkReceivedAsync(firstId, cancellationToken)).IsSuccess.ShouldBeTrue();
            (await service.MarkReceivedAsync(firstId, cancellationToken)).IsSuccess.ShouldBeTrue();

            (await dbContext.Set<OutboxMessage>().CountAsync(
                message => message.EventType == GoodsReceived.EventType,
                cancellationToken)).ShouldBe(1);
            (await dbContext.PurchaseItems.SingleAsync(
                item => item.Id == firstId,
                cancellationToken)).ReceivedAt.ShouldBe(Now);

            await InstallRejectingOutboxTriggerAsync(
                connectionString,
                GoodsReceived.EventType,
                cancellationToken);
            await Should.ThrowAsync<DbUpdateException>(() =>
                service.MarkReceivedAsync(rollbackId, cancellationToken));
        }

        await using var verification = new ProcurementDbContext(options);
        (await verification.PurchaseItems.AsNoTracking().SingleAsync(
            item => item.Id == rollbackId,
            cancellationToken)).ReceivedAt.ShouldBeNull();
        (await verification.Set<OutboxMessage>().CountAsync(
            message => message.EventType == GoodsReceived.EventType,
            cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "TripCostRecorded：成本與 outbox 同交易，command 重送只發一次")]
    public async Task Campaign_trip_cost_is_idempotent_and_atomic_with_outbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync("campaign", cancellationToken);
        var options = new DbContextOptionsBuilder<CampaignDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var campaignId = CampaignId.New();
        var firstCostId = TripCostId.New();
        var rollbackCostId = TripCostId.New();

        await using (var dbContext = new CampaignDbContext(options))
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            var campaign = CampaignAggregate.CreateDraft(
                campaignId,
                TenantId.Default,
                new CampaignDraftInput(
                    "東京採購",
                    "Tokyo",
                    new DateOnly(2026, 9, 10),
                    new DateOnly(2026, 9, 12),
                    Now.AddDays(5),
                    null,
                    null),
                Now).Value;
            dbContext.Campaigns.Add(campaign);
            await dbContext.SaveChangesAsync(cancellationToken);

            var correlation = new MutableCorrelation { TenantId = TenantId.Default };
            var service = new CampaignService(
                new CampaignRepository(dbContext),
                dbContext,
                new OutboxEventPublisher<CampaignDbContext>(
                    dbContext,
                    correlation,
                    EventTypeRegistry.FromAssemblies([typeof(TripCostRecorded).Assembly])),
                null!,
                new M1b3CampaignOrderQuery(),
                new FixedClock(Now),
                correlation);
            var input = new TripCostInput(
                firstCostId,
                TripCostKind.Airfare,
                Money.OfMajor(12_000, Currency.TWD),
                "來回機票");

            var first = await service.RecordTripCostAsync(campaignId, input, cancellationToken);
            var replay = await service.RecordTripCostAsync(campaignId, input, cancellationToken);

            first.IsSuccess.ShouldBeTrue();
            replay.IsSuccess.ShouldBeTrue();
            replay.Value.TripCostTotal.ShouldBe(Money.OfMajor(12_000, Currency.TWD));
            (await dbContext.TripCosts.CountAsync(cancellationToken)).ShouldBe(1);
            (await dbContext.Set<OutboxMessage>().CountAsync(
                message => message.EventType == TripCostRecorded.EventType,
                cancellationToken)).ShouldBe(1);

            await InstallRejectingOutboxTriggerAsync(
                connectionString,
                TripCostRecorded.EventType,
                cancellationToken);
            await Should.ThrowAsync<DbUpdateException>(() =>
                service.RecordTripCostAsync(
                    campaignId,
                    new TripCostInput(
                        rollbackCostId,
                        TripCostKind.LocalTransport,
                        Money.OfMajor(800, Currency.TWD),
                        "機場交通"),
                    cancellationToken));
        }

        await using var verification = new CampaignDbContext(options);
        (await verification.TripCosts.AsNoTracking().CountAsync(
            cost => cost.Id == rollbackCostId,
            cancellationToken)).ShouldBe(0);
        (await verification.Set<OutboxMessage>().CountAsync(
            message => message.EventType == TripCostRecorded.EventType,
            cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "OrderReadyToShip：最後一條預購收貨才發一次，且保留現貨付款捷徑")]
    public async Task Ordering_receipt_is_idempotent_atomic_and_preserves_stock_shortcut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync("ordering", cancellationToken);
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        OrderLineId rollbackLineId;

        await using (var dbContext = new OrderingDbContext(options))
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            var order = CreatePaidOrder(preorderCount: 2);
            var rollbackOrder = CreatePaidOrder(preorderCount: 1);
            rollbackLineId = rollbackOrder.Lines.Single().Id;
            dbContext.Orders.AddRange(order, rollbackOrder);
            await dbContext.SaveChangesAsync(cancellationToken);

            var stock = CreateUnpaidOrder(preorderCount: 0);
            stock.CapturePayment(stock.GrandTotal).Value.ShouldBe(PaymentCaptureTransition.ReadyToShip);
            stock.Status.ShouldBe(OrderStatus.ReadyToShip);

            var correlation = new MutableCorrelation { TenantId = TenantId.Default };
            var service = new OrderingApplicationService(
                new OrderingRepository(dbContext),
                dbContext,
                new OutboxEventPublisher<OrderingDbContext>(
                    dbContext,
                    correlation,
                    EventTypeRegistry.FromAssemblies([typeof(OrderReadyToShip).Assembly])),
                null!,
                new FixedClock(Now),
                correlation);
            var firstLineId = order.Lines[0].Id;
            var lastLineId = order.Lines[1].Id;

            (await service.RecordGoodsReceivedAsync(firstLineId, cancellationToken))
                .IsSuccess.ShouldBeTrue();
            (await service.RecordGoodsReceivedAsync(firstLineId, cancellationToken))
                .IsSuccess.ShouldBeTrue();
            order.Status.ShouldBe(OrderStatus.Purchasing);
            (await dbContext.Set<OutboxMessage>().CountAsync(
                message => message.EventType == OrderReadyToShip.EventType,
                cancellationToken)).ShouldBe(0);

            (await service.RecordGoodsReceivedAsync(lastLineId, cancellationToken))
                .IsSuccess.ShouldBeTrue();
            (await service.RecordGoodsReceivedAsync(lastLineId, cancellationToken))
                .IsSuccess.ShouldBeTrue();
            order.Status.ShouldBe(OrderStatus.ReadyToShip);
            (await dbContext.Set<OutboxMessage>().CountAsync(
                message => message.EventType == OrderReadyToShip.EventType,
                cancellationToken)).ShouldBe(1);

            await InstallRejectingOutboxTriggerAsync(
                connectionString,
                OrderReadyToShip.EventType,
                cancellationToken);
            await Should.ThrowAsync<DbUpdateException>(() =>
                service.RecordGoodsReceivedAsync(rollbackLineId, cancellationToken));
        }

        await using var verification = new OrderingDbContext(options);
        var rollbackLine = await verification.OrderLines.AsNoTracking().SingleAsync(
            line => line.Id == rollbackLineId,
            cancellationToken);
        rollbackLine.GoodsReceivedAt.ShouldBeNull();
        (await verification.Orders.AsNoTracking().SingleAsync(
            candidate => candidate.Id == rollbackLine.OrderId,
            cancellationToken)).Status.ShouldBe(OrderStatus.Purchasing);
        (await verification.Set<OutboxMessage>().CountAsync(
            message => message.EventType == OrderReadyToShip.EventType,
            cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "0009 M1b seams 可重跑，owner 與 tenant composite FK 完整")]
    public async Task Migration_is_idempotent_owned_and_tenant_safe()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync("migration", cancellationToken);
        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");

        await ExecuteMigrationChainAsync(connectionString, migrations, 9, cancellationToken);
        await ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0009_m1b_seams.sql"),
            cancellationToken);

        (await ScalarAsync<string>(connectionString, """
            SELECT tableowner
            FROM pg_tables
            WHERE schemaname = 'campaign' AND tablename = 'trip_cost';
            """, cancellationToken)).ShouldBe("greygray_owner");
        (await ScalarAsync<int>(connectionString, """
            SELECT count(*)::int
            FROM information_schema.columns
            WHERE (table_schema, table_name, column_name) IN (
                ('procurement', 'purchase_item', 'received_at'),
                ('campaign', 'trip_cost', 'amount_minor'),
                ('ordering', 'order_line', 'goods_received_at'));
            """, cancellationToken)).ShouldBe(3);
        (await ScalarAsync<int>(connectionString, """
            SELECT count(*)::int
            FROM pg_constraint
            WHERE conrelid = 'campaign.trip_cost'::regclass
              AND conname = 'trip_cost_campaign_same_tenant_fk';
            """, cancellationToken)).ShouldBe(1);

        var campaignId = Guid.CreateVersion7();
        await ExecuteSqlAsync(connectionString, $"""
            INSERT INTO campaign.campaign (
                id, tenant_id, title, destination, depart_at, return_at, closes_at,
                status, created_at, updated_at)
            VALUES (
                '{campaignId}'::uuid, '{TenantA}'::uuid, 'M1b campaign', 'Tokyo',
                current_date + 10, current_date + 12, now() + interval '5 days',
                0, now(), now());
            """, cancellationToken);
        var crossTenant = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteSqlAsync(connectionString, $"""
                INSERT INTO campaign.trip_cost (
                    id, tenant_id, campaign_id, kind, amount_minor, currency, memo, recorded_at)
                VALUES (
                    '{Guid.CreateVersion7()}'::uuid, '{TenantB}'::uuid, '{campaignId}'::uuid,
                    1, 1000, 'TWD', '', now());
                """, cancellationToken));
        crossTenant.SqlState.ShouldBe("23503");
    }

    private static PurchaseItemAggregate CreatePurchasedItem(MoneyPair paid)
    {
        var item = PurchaseItemAggregate.Create(
            PurchaseItemId.New(),
            TenantId.Default,
            CampaignId.New(),
            SkuId.New(),
            OrderLineId.New(),
            2,
            Money.OfMajor(350, Currency.TWD),
            Now).Value;
        item.MarkPurchased(2, paid, Now).IsSuccess.ShouldBeTrue();
        return item;
    }

    private static Order CreatePaidOrder(int preorderCount)
    {
        var order = CreateUnpaidOrder(preorderCount);
        order.CapturePayment(order.GrandTotal).IsSuccess.ShouldBeTrue();
        foreach (var line in order.Lines.Where(line => line.Mode == FulfillmentMode.Preorder))
        {
            order.RecordItemPurchased(line.Id, line.Quantity).IsSuccess.ShouldBeTrue();
        }

        return order;
    }

    internal static Order CreateUnpaidOrder(int preorderCount)
    {
        var snapshot = new PricingSnapshot(
            PricingSnapshotId.New(),
            DeliveryMethod.ConvenienceStore,
            400,
            0,
            400,
            Money.OfMajor(60, Currency.TWD),
            FeeRuleSetId.New(),
            FeeRuleId.New(),
            ShippingStrategyKind.Flat,
            ["測試運費"],
            Now);
        var lines = preorderCount == 0
            ?
            [
                new CheckoutLine(
                    SkuId.New(),
                    FulfillmentMode.Stock,
                    null,
                    null,
                    1,
                    Money.OfMajor(100, Currency.TWD)),
            ]
            : Enumerable.Range(0, preorderCount)
                .Select(_ => new CheckoutLine(
                    SkuId.New(),
                    FulfillmentMode.Preorder,
                    CampaignId.New(),
                    CampaignOfferId.New(),
                    1,
                    Money.OfMajor(100, Currency.TWD)))
                .ToArray();
        var checkout = new CheckoutCompleted(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            CartId.New(),
            CustomerId.New(),
            null,
            DeliveryMethod.ConvenienceStore,
            ShippingPolicy.HoldUntilComplete,
            snapshot.Id,
            lines,
            $"m1b3-{Guid.CreateVersion7():N}");
        return Order.Place(new OrderId(Guid.NewGuid()), checkout, snapshot, Now).Value;
    }

    private async Task<string> CreateDatabaseAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        var databaseName = $"m1b3_{prefix}_{Guid.NewGuid():N}";
        await ExecuteSqlAsync(
            _postgres.GetConnectionString(),
            $"CREATE DATABASE \"{databaseName}\";",
            cancellationToken);
        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName,
        };
        return builder.ConnectionString;
    }

    private static async Task InstallRejectingOutboxTriggerAsync(
        string connectionString,
        string eventType,
        CancellationToken cancellationToken) =>
        await ExecuteSqlAsync(connectionString, $"""
            CREATE OR REPLACE FUNCTION platform.reject_m1b3_outbox()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.event_type = '{eventType}' THEN
                    RAISE EXCEPTION 'forced M1b-3 rollback';
                END IF;
                RETURN NEW;
            END
            $$;
            DROP TRIGGER IF EXISTS reject_m1b3_outbox ON platform.outbox_message;
            CREATE TRIGGER reject_m1b3_outbox
                BEFORE INSERT ON platform.outbox_message
                FOR EACH ROW EXECUTE FUNCTION platform.reject_m1b3_outbox();
            """, cancellationToken);

    private static async Task ExecuteMigrationChainAsync(
        string connectionString,
        string migrationDirectory,
        int lastMigration,
        CancellationToken cancellationToken)
    {
        var paths = Directory.GetFiles(migrationDirectory, "*.sql")
            .Where(path => int.TryParse(Path.GetFileName(path).AsSpan(0, 4), out var number)
                && number <= lastMigration)
            .OrderBy(path => path, StringComparer.Ordinal);
        foreach (var path in paths)
        {
            await ExecuteScriptAsync(connectionString, path, cancellationToken);
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

internal sealed class M1b3CampaignOrderQuery : ICampaignOrderQuery
{
    public Task<Result<CampaignOrderSnapshot>> GetAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<CampaignOrderSnapshot>.Success(
            new CampaignOrderSnapshot(0, new Dictionary<SkuId, int>(), false)));
}
