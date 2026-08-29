using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Modules.Procurement.Core;
using GreyGray.Modules.Procurement.Infra;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Outbox;
using GreyGray.Platform.Saga;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1b.Procurement.Tests;

/// <summary>
/// M1b-2：缺貨補償與現場漲價詢問。用真的 PostgreSQL 驗證 CHECK constraint、
/// tenant composite FK 與 partial unique index——這些是 EF InMemory 不會執行的東西。
/// </summary>
public sealed class ProcurementCompensationPostgresTests : IAsyncLifetime
{
    private const string TenantA = "10000000-0000-0000-0000-000000000001";
    private const string TenantB = "20000000-0000-0000-0000-000000000002";

    private static readonly DateTimeOffset Now =
        new(2026, 8, 30, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "標記缺貨：不承載退款去向、同交易發一次 ItemUnavailable，已買到不能再標")]
    public async Task Mark_unavailable_is_idempotent_atomic_and_blocks_purchased()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (dbContext, service, _) = await CreateServiceAsync(cancellationToken);
        await using (dbContext)
        {
            var pending = CreatePendingItem();
            var purchased = CreatePendingItem();
            purchased.MarkPurchased(
                purchased.QuantityRequested,
                new MoneyPair(Money.OfMajor(500, Currency.JPY), Money.OfMajor(110, Currency.TWD), null),
                Now).IsSuccess.ShouldBeTrue();
            dbContext.PurchaseItems.AddRange(pending, purchased);
            await dbContext.SaveChangesAsync(cancellationToken);

            var first = await service.MarkUnavailableAsync(pending.Id, "缺貨", cancellationToken);
            var replay = await service.MarkUnavailableAsync(pending.Id, "缺貨", cancellationToken);

            first.IsSuccess.ShouldBeTrue();
            first.Value.Status.ShouldBe(PurchaseItemStatus.Unavailable);
            first.Value.DecidedAt.ShouldBe(Now);
            replay.IsSuccess.ShouldBeTrue();
            (await dbContext.Set<OutboxMessage>().CountAsync(
                message => message.EventType == ItemUnavailable.EventType,
                cancellationToken)).ShouldBe(1);

            var blocked = await service.MarkUnavailableAsync(purchased.Id, "缺貨", cancellationToken);
            blocked.Error.Code.ShouldBe("procurement.purchase-item-already-purchased");
        }
    }

    [Fact(DisplayName = "現場漲價：開一輪詢問、排 saga timer、逾時視為照買且與客人明講回覆共用解決邏輯")]
    public async Task Report_price_changed_schedules_timer_and_resolves_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (dbContext, service, _) = await CreateServiceAsync(cancellationToken);
        await using (dbContext)
        {
            var item = CreatePendingItem();
            dbContext.PurchaseItems.Add(item);
            await dbContext.SaveChangesAsync(cancellationToken);

            var newPrice = Money.OfMajor(420, Currency.TWD);
            var reported = await service.ReportPriceChangedAsync(item.Id, newPrice, cancellationToken);
            reported.IsSuccess.ShouldBeTrue();
            reported.Value.NewPrice.ShouldBe(newPrice);

            (await dbContext.Set<OutboxMessage>().CountAsync(
                message => message.EventType == ItemPriceChanged.EventType,
                cancellationToken)).ShouldBe(1);
            (await dbContext.PurchaseItems.AsNoTracking().SingleAsync(
                candidate => candidate.Id == item.Id, cancellationToken))
                .Status.ShouldBe(PurchaseItemStatus.PriceChangedPendingConfirmation);

            // 同一品項不能同時開第二輪。
            var duplicate = await service.ReportPriceChangedAsync(item.Id, newPrice, cancellationToken);
            duplicate.Error.Code.ShouldBe("procurement.price-change-already-pending");

            // 逾時視為照買：解決一次之後品項放回 Pending，可以重新回報。
            await service.ResolveInquiryTimeoutAsync(reported.Value.Id, cancellationToken);
            (await dbContext.PurchaseItems.AsNoTracking().SingleAsync(
                candidate => candidate.Id == item.Id, cancellationToken))
                .Status.ShouldBe(PurchaseItemStatus.Pending);
            (await dbContext.Set<OutboxMessage>().CountAsync(
                message => message.EventType == InquiryResolved.EventType,
                cancellationToken)).ShouldBe(1);

            // 逾時之後客人才回：以第一次（逾時）為準，不再改變結果、不再發第二次事件。
            await service.ReplyAsync(reported.Value.Id, accepted: true, "OK", cancellationToken);
            (await dbContext.Set<OutboxMessage>().CountAsync(
                message => message.EventType == InquiryResolved.EventType,
                cancellationToken)).ShouldBe(1);
            var inquiryAfterReply = await service.GetInquiryAsync(reported.Value.Id, cancellationToken);
            inquiryAfterReply.Value.Outcome.ShouldBe(InquiryOutcome.AutoApprovedOnTimeout);
        }
    }

    [Fact(DisplayName = "0011：可重跑、owner 正確、tenant composite FK 擋跨租戶、開放中的詢問只能有一輪")]
    public async Task Migration_is_idempotent_owned_tenant_safe_and_limits_open_inquiries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync("migration11", cancellationToken);
        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");

        await ExecuteMigrationChainAsync(connectionString, migrations, 11, cancellationToken);
        await ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0011_m1b_compensation.sql"),
            cancellationToken);

        (await ScalarAsync<string>(connectionString, """
            SELECT tableowner FROM pg_tables
            WHERE schemaname = 'procurement' AND tablename = 'inquiry';
            """, cancellationToken)).ShouldBe("greygray_owner");
        (await ScalarAsync<int>(connectionString, """
            SELECT count(*)::int FROM pg_constraint
            WHERE conrelid = 'procurement.inquiry'::regclass
              AND conname = 'inquiry_purchase_item_same_tenant_fk';
            """, cancellationToken)).ShouldBe(1);

        var purchaseItemId = Guid.CreateVersion7();
        await ExecuteSqlAsync(connectionString, $"""
            INSERT INTO procurement.purchase_item (
                id, tenant_id, campaign_id, sku_id, order_line_id,
                quantity_requested, status, created_at)
            VALUES (
                '{purchaseItemId}'::uuid, '{TenantA}'::uuid, '{Guid.CreateVersion7()}'::uuid,
                '{Guid.CreateVersion7()}'::uuid, '{Guid.CreateVersion7()}'::uuid, 1, 0, now());
            """, cancellationToken);

        var crossTenant = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteSqlAsync(connectionString, $"""
                INSERT INTO procurement.inquiry (
                    id, tenant_id, purchase_item_id,
                    original_price_amount_minor, original_price_currency,
                    new_price_amount_minor, new_price_currency, asked_at, timeout_at)
                VALUES (
                    '{Guid.CreateVersion7()}'::uuid, '{TenantB}'::uuid, '{purchaseItemId}'::uuid,
                    35000, 'TWD', 42000, 'TWD', now(), now() + interval '2 hours');
                """, cancellationToken));
        crossTenant.SqlState.ShouldBe("23503");

        await ExecuteSqlAsync(connectionString, $"""
            INSERT INTO procurement.inquiry (
                id, tenant_id, purchase_item_id,
                original_price_amount_minor, original_price_currency,
                new_price_amount_minor, new_price_currency, asked_at, timeout_at)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{TenantA}'::uuid, '{purchaseItemId}'::uuid,
                35000, 'TWD', 42000, 'TWD', now(), now() + interval '2 hours');
            """, cancellationToken);
        var secondOpenInquiry = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteSqlAsync(connectionString, $"""
                INSERT INTO procurement.inquiry (
                    id, tenant_id, purchase_item_id,
                    original_price_amount_minor, original_price_currency,
                    new_price_amount_minor, new_price_currency, asked_at, timeout_at)
                VALUES (
                    '{Guid.CreateVersion7()}'::uuid, '{TenantA}'::uuid, '{purchaseItemId}'::uuid,
                    35000, 'TWD', 44000, 'TWD', now(), now() + interval '2 hours');
                """, cancellationToken));
        secondOpenInquiry.SqlState.ShouldBe("23505");

        // 重跑必須乾淨過關（idempotent）。
        await ExecuteScriptAsync(
            connectionString,
            Path.Combine(migrations, "0011_m1b_compensation.sql"),
            cancellationToken);
    }

    private static PurchaseItemAggregate CreatePendingItem() =>
        PurchaseItemAggregate.Create(
            PurchaseItemId.New(),
            TenantId.Default,
            CampaignId.New(),
            SkuId.New(),
            OrderLineId.New(),
            2,
            Money.OfMajor(350, Currency.TWD),
            Now).Value;

    private async Task<(ProcurementDbContext DbContext, ProcurementApplicationService Service, string ConnectionString)>
        CreateServiceAsync(CancellationToken cancellationToken)
    {
        var connectionString = await CreateDatabaseAsync("compensation", cancellationToken);
        var options = new DbContextOptionsBuilder<ProcurementDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var dbContext = new ProcurementDbContext(options);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        var correlation = new MutableCorrelation { TenantId = TenantId.Default };
        var service = new ProcurementApplicationService(
            new ProcurementRepository(dbContext),
            new InquiryRepository(dbContext),
            dbContext,
            new OutboxEventPublisher<ProcurementDbContext>(
                dbContext,
                correlation,
                EventTypeRegistry.FromAssemblies(
                    [typeof(ItemUnavailable).Assembly, typeof(OrderReadyToShip).Assembly])),
            new SagaTimerScheduler<ProcurementDbContext>(dbContext, new FixedClock(Now)),
            new EmptyOrderQuery(),
            new EmptyCampaignQuery(),
            new FixedClock(Now),
            correlation);
        return (dbContext, service, connectionString);
    }

    private async Task<string> CreateDatabaseAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        var databaseName = $"m1b2_{prefix}_{Guid.NewGuid():N}";
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

internal sealed class EmptyOrderQuery : IOrderQuery
{
    public Task<Result<OrderView>> GetAsync(OrderId id, CancellationToken cancellationToken) =>
        Task.FromResult(Result<OrderView>.Failure("ordering.order-not-found", "找不到訂單。"));

    public Task<Result<IReadOnlyList<OrderView>>> GetByCampaignAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<OrderView>>.Success(Array.Empty<OrderView>()));
}

internal sealed class EmptyCampaignQuery : ICampaignQuery
{
    public Task<Result<CampaignSummary>> GetAsync(CampaignId id, CancellationToken cancellationToken) =>
        Task.FromResult(Result<CampaignSummary>.Failure("campaign.not-found", "找不到開團。"));

    public Task<Result<CampaignOffer>> GetOfferAsync(
        CampaignOfferId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<CampaignOffer>.Failure("campaign.offer-not-found", "找不到開團商品。"));

    public Task<Result<bool>> IsAcceptingOrdersAsync(CampaignId id, CancellationToken cancellationToken) =>
        Task.FromResult(Result<bool>.Success(false));
}
