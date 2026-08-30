using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

/// <summary>
/// 守住「付款成功的訂單不得留下 <c>refunded_currency</c>」這條不變式。
/// </summary>
/// <remarks>
/// 為什麼要有這一支：<c>Order.CapturePayment</c> 曾在沒有任何退款時就把
/// <c>RefundedCurrency</c> 設成非 null，違反 <c>orders_refunded_consistent</c>，
/// 而 172 條測試沒有一條抓到——因為所有 Ordering 整合測試都用
/// <c>EnsureCreatedAsync()</c> 建 schema，而 EF model 當時沒有宣告那條 check constraint。
/// 所以這裡刻意跑兩次同一個情境：一次在 EF model 建的 schema 上（證明 EF model 補齊了），
/// 一次在真的套過 <c>db/migrations</c> 的 schema 上（證明正式資料庫上也過得了）。
/// 兩邊都先斷言 constraint 真的存在，避免變成「查了零個對象」的空跑。
/// </remarks>
public sealed class OrderingPaymentConstraintTests : IAsyncLifetime
{
    /// <summary>目前 db/migrations 的最後一個編號；測試要跑在正式機會有的完整 schema 上。</summary>
    private const int LastMigration = 14;

    private static readonly DateTimeOffset Now = new(2026, 8, 30, 3, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName =
        "EF model 建的 schema 帶著 orders_refunded_consistent，且 CapturePayment 存回後 refunded_currency 是 NULL")]
    public async Task Capture_payment_leaves_refunded_currency_null_on_ef_created_schema()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync("ordering_capture_ef", cancellationToken);
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var dbContext = new OrderingDbContext(options);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        // 先決條件：EnsureCreatedAsync 產生的 schema 真的有這三條約束。
        // 少了它們，下面的斷言即使全綠也只是在空跑——那正是這個 bug 活下來的原因。
        foreach (var constraint in new[]
                 {
                     "orders_currency_consistent",
                     "orders_paid_consistent",
                     "orders_refunded_consistent",
                 })
        {
            (await ConstraintExistsAsync(connectionString, constraint, cancellationToken))
                .ShouldBeTrue($"EF model 沒有宣告 {constraint}，EnsureCreatedAsync 的 schema 就守不住它。");
        }

        var orderId = await PlaceAndCapturePaymentAsync(
            dbContext,
            "capture-ef-schema",
            cancellationToken);

        var persisted = await dbContext.Orders.AsNoTracking()
            .SingleAsync(order => order.Id == orderId, cancellationToken);
        persisted.PaidAmountMinor.ShouldBe(persisted.GrandTotalAmountMinor);
        persisted.PaidCurrency.ShouldBe(persisted.GrandTotalCurrency);
        persisted.RefundedAmountMinor.ShouldBe(0L);
        persisted.RefundedCurrency.ShouldBeNull(
            "沒有發生任何退款，CapturePayment 不該碰 RefundedCurrency。");
    }

    [Fact(DisplayName = "真的套過 0001~0014 的 schema 上完成一次結帳付款，不噴 23514")]
    public async Task Capture_payment_survives_the_real_migrated_schema()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(cancellationToken);

        // 先決條件：這是真的 migration 建的 schema，不是 EnsureCreatedAsync 的。
        (await ConstraintExistsAsync(connectionString, "orders_refunded_consistent", cancellationToken))
            .ShouldBeTrue("套完 migration 卻沒有 orders_refunded_consistent，這條測試就沒有在測東西。");

        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var dbContext = new OrderingDbContext(options);

        // 這裡刻意不呼叫 EnsureCreatedAsync——schema 完全來自 db/migrations。
        var orderId = await PlaceAndCapturePaymentAsync(
            dbContext,
            "capture-migrated-schema",
            cancellationToken);

        var sql = $"""
            SELECT count(*) FROM ordering.orders
            WHERE id = '{orderId}'::uuid
              AND paid_amount_minor = grand_total_amount_minor
              AND paid_currency = grand_total_currency
              AND refunded_amount_minor = 0
              AND refunded_currency IS NULL;
            """;
        (await ScalarAsync<long>(connectionString, sql, cancellationToken)).ShouldBe(
            1L,
            "真實 schema 上付款後的訂單列不符合 orders_refunded_consistent 的前提。");
    }

    /// <summary>建立訂單並完成付款；付款那一步若違反 check constraint，SaveChanges 就會噴 23514。</summary>
    private static async Task<OrderId> PlaceAndCapturePaymentAsync(
        OrderingDbContext dbContext,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var clock = new FakeClock(Now);
        var correlation = new FakeCorrelation();
        var pricing = new FakePricing(clock);
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
        pricing.Seed(snapshot);
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
            [
                new CheckoutLine(
                    SkuId.New(), FulfillmentMode.Preorder,
                    CampaignId.New(), CampaignOfferId.New(), 1,
                    Money.OfMajor(100, Currency.TWD)),
            ],
            idempotencyKey);
        var publisher = new OutboxEventPublisher<OrderingDbContext>(
            dbContext,
            correlation,
            EventTypeRegistry.FromAssemblies([typeof(OrderPlaced).Assembly]));
        var service = new OrderingApplicationService(
            new OrderingRepository(dbContext),
            dbContext,
            publisher,
            pricing,
            clock,
            correlation);

        var created = await service.CreateFromCheckoutAsync(checkout, cancellationToken);
        created.IsSuccess.ShouldBeTrue("先決條件：建立訂單就失敗了。");

        var captured = await service.RecordPaymentCapturedAsync(
            created.Value.Id,
            created.Value.GrandTotal,
            cancellationToken);
        captured.IsSuccess.ShouldBeTrue("先決條件：記錄付款失敗。");

        return created.Value.Id;
    }

    private async Task<string> CreateMigratedDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = await CreateDatabaseAsync(
            "ordering_capture_migrated",
            cancellationToken);
        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        for (var migration = 1; migration <= LastMigration; migration++)
        {
            var path = Directory.GetFiles(migrations, $"{migration:0000}_*.sql").ShouldHaveSingleItem();

            // psql 的 meta command（反斜線開頭）不能經 Npgsql 送，照既有 migration 測試的做法濾掉。
            var sql = string.Join(
                Environment.NewLine,
                File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('\\')));
            await ExecuteSqlAsync(connectionString, sql, cancellationToken);
        }

        return connectionString;
    }

    private async Task<string> CreateDatabaseAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        var databaseName = $"{prefix}_{Guid.NewGuid():N}";
        await ExecuteSqlAsync(
            _postgres.GetConnectionString(),
            $"CREATE DATABASE \"{databaseName}\";",
            cancellationToken);
        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private static Task<bool> ConstraintExistsAsync(
        string connectionString,
        string constraintName,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conrelid = 'ordering.orders'::regclass
                  AND conname = '{constraintName}');
            """;
        return ScalarAsync<bool>(connectionString, sql, cancellationToken);
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
        return (T)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("查詢未回傳 scalar。"));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GreyGray.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("找不到 GreyGray.slnx。");
    }
}
