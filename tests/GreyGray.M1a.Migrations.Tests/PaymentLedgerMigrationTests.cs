using Npgsql;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Payment.Core;
using GreyGray.Modules.Payment.Infra;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1a.Migrations.Tests;

public sealed partial class PaymentLedgerMigrationTests : IAsyncLifetime
{
    private const string TenantId = "00000000-0000-0000-0000-000000000001";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "Ledger 同交易可建立平衡分錄，commit 封存後禁止追加 line")]
    public async Task Ledger_finalizes_valid_entry_and_rejects_late_lines()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(5, cancellationToken);
        var entryId = Guid.CreateVersion7();

        await ExecuteInTransactionAsync(connectionString, $"""
            INSERT INTO ledger.journal_entry (
                id, tenant_id, occurred_at, posted_at, source_module, source_ref, memo)
            VALUES (
                '{entryId}'::uuid, '{TenantId}'::uuid, now(), now(),
                'Payment', 'payment-valid', 'valid');
            INSERT INTO ledger.journal_line (
                id, tenant_id, entry_id, account_id, account_code,
                direction, amount_minor, currency)
            VALUES
                ('{Guid.CreateVersion7()}'::uuid, '{TenantId}'::uuid, '{entryId}'::uuid,
                 '10000000-0000-0000-0000-000000001151'::uuid, '1151', 1, 10000, 'TWD'),
                ('{Guid.CreateVersion7()}'::uuid, '{TenantId}'::uuid, '{entryId}'::uuid,
                 '10000000-0000-0000-0000-000000002110'::uuid, '2110', 2, 10000, 'TWD');
            """, cancellationToken);

        (await ScalarAsync<bool>(connectionString, $"""
            SELECT is_posted FROM ledger.journal_entry WHERE id = '{entryId}'::uuid;
            """, cancellationToken)).ShouldBeTrue();

        var lateLine = await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync(
            connectionString,
            $"""
            INSERT INTO ledger.journal_line (
                id, tenant_id, entry_id, account_id, account_code,
                direction, amount_minor, currency)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{TenantId}'::uuid, '{entryId}'::uuid,
                '10000000-0000-0000-0000-000000001100'::uuid, '1100', 1, 1, 'TWD');
            """,
            cancellationToken));
        lateLine.SqlState.ShouldBe("P0001");
        lateLine.MessageText.ShouldContain("已封存");

        await ExecuteMigrationAsync(connectionString, 5, cancellationToken);
        (await ScalarAsync<bool>(connectionString, $"""
            SELECT is_posted FROM ledger.journal_entry WHERE id = '{entryId}'::uuid;
            """, cancellationToken)).ShouldBeTrue();
    }

    [Fact(DisplayName = "Ledger 空分錄與借貸不平衡都在 commit 前失敗")]
    public async Task Ledger_rejects_empty_and_unbalanced_entries_at_commit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(5, cancellationToken);

        var empty = await Should.ThrowAsync<PostgresException>(() => ExecuteInTransactionAsync(
            connectionString,
            $"""
            INSERT INTO ledger.journal_entry (
                id, tenant_id, occurred_at, posted_at, source_module, source_ref, memo)
            VALUES (
                '{Guid.CreateVersion7()}'::uuid, '{TenantId}'::uuid, now(), now(),
                'Payment', 'payment-empty', 'empty');
            """,
            cancellationToken));
        empty.SqlState.ShouldBe("P0001");
        empty.MessageText.ShouldContain("至少需要兩條");

        var entryId = Guid.CreateVersion7();
        var unbalanced = await Should.ThrowAsync<PostgresException>(() => ExecuteInTransactionAsync(
            connectionString,
            $"""
            INSERT INTO ledger.journal_entry (
                id, tenant_id, occurred_at, posted_at, source_module, source_ref, memo)
            VALUES (
                '{entryId}'::uuid, '{TenantId}'::uuid, now(), now(),
                'Payment', 'payment-unbalanced', 'unbalanced');
            INSERT INTO ledger.journal_line (
                id, tenant_id, entry_id, account_id, account_code,
                direction, amount_minor, currency)
            VALUES
                ('{Guid.CreateVersion7()}'::uuid, '{TenantId}'::uuid, '{entryId}'::uuid,
                 '10000000-0000-0000-0000-000000001151'::uuid, '1151', 1, 10000, 'TWD'),
                ('{Guid.CreateVersion7()}'::uuid, '{TenantId}'::uuid, '{entryId}'::uuid,
                 '10000000-0000-0000-0000-000000002110'::uuid, '2110', 2, 9999, 'TWD');
            """,
            cancellationToken));
        unbalanced.SqlState.ShouldBe("P0001");
        unbalanced.MessageText.ShouldContain("借貸不平衡");
    }

    [Fact(DisplayName = "0005 遇到無法推導的舊 payment seam row 明確失敗")]
    public async Task Payment_upgrade_fails_instead_of_fabricating_legacy_snapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(4, cancellationToken);
        await ExecuteSqlAsync(connectionString, $"""
            INSERT INTO payment.payment (id, tenant_id, provider)
            VALUES ('{Guid.CreateVersion7()}'::uuid, '{TenantId}'::uuid, 1);
            """, cancellationToken);

        var failure = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteMigrationAsync(connectionString, 5, cancellationToken));

        failure.SqlState.ShouldBe("P0001");
        failure.MessageText.ShouldContain("無法安全推導");
    }

    [Fact(DisplayName = "同 tenant/order 併發只能建立一筆 active payment")]
    public async Task Concurrent_active_payments_are_unique()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(5, cancellationToken);
        var orderId = Guid.CreateVersion7();

        var results = await Task.WhenAll(
            TryInsertPendingPaymentAsync(connectionString, orderId, "GGA", cancellationToken),
            TryInsertPendingPaymentAsync(connectionString, orderId, "GGB", cancellationToken));

        results.Count(success => success).ShouldBe(1);
        (await ScalarAsync<int>(connectionString, $"""
            SELECT count(*)::int
            FROM payment.payment
            WHERE tenant_id = '{TenantId}'::uuid
              AND order_id = '{orderId}'::uuid
              AND status IN (0, 1, 4);
            """, cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "EF 先封存過期 Pending，再建立新 payment，不受 immediate unique 排序影響")]
    public async Task Ef_retries_after_expired_pending_without_hitting_active_unique()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(24, cancellationToken);
        var now = new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);
        var orderId = OrderId.New();
        await using (var seed = CreatePaymentDbContext(connectionString))
        {
            seed.Payments.Add(Payment.Start(
                PaymentId.New(),
                GreyGray.Shared.Kernel.TenantId.Default,
                orderId,
                Money.OfMajor(100, Currency.TWD),
                Money.OfMajor(60, Currency.TWD),
                "GGEXPIRED0000000001",
                now.AddHours(-1),
                now.AddMinutes(-30)));
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using (var context = CreatePaymentDbContext(connectionString))
        {
            var service = new PaymentApplicationService(
                new PaymentRepository(context),
                context,
                new NoopPublisher(),
                new StubGateway(),
                Settings(),
                new StubClock(now),
                new StubCorrelationContext());
            var result = await service.InitiateAsync(
                new PaymentInitiationRequest(
                    orderId,
                    Money.OfMajor(100, Currency.TWD),
                    Money.OfMajor(60, Currency.TWD),
                    "retry",
                    new Uri("https://example.test/payment-return"),
                    new Uri("https://example.test/payment/result?orderId=1")),
                cancellationToken);
            result.IsSuccess.ShouldBeTrue();
        }

        await using (var verify = CreatePaymentDbContext(connectionString))
        {
            var statuses = await verify.Payments
                .Where(payment => payment.OrderId == orderId)
                .Select(payment => payment.Status)
                .OrderBy(status => status)
                .ToArrayAsync(cancellationToken);
            statuses.ShouldBe([PaymentStatus.Pending, PaymentStatus.Failed], ignoreOrder: true);
        }
    }

    /// <summary>
    /// 綠界回呼在<b>真的 Postgres</b> 上把付款寫成 Captured。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 這條的存在理由是 BE-40 第二輪：Leader 用 <c>-UseEcpaySimulator</c> 起真環境走完整條路，
    /// 回呼端點回 500——
    /// <c>Cannot write DateTimeOffset with Offset=08:00:00 to PostgreSQL type
    /// 'timestamp with time zone', only offset 0 (UTC) is supported.</c>
    /// 綠界的 <c>PaymentDate</c> 是台北的牆上時間，<c>TryGetCallbackTime</c> 把它組成 +08:00 的
    /// <see cref="DateTimeOffset"/>，一路傳到 <c>CapturedAt</c>（<c>timestamptz</c>），Npgsql 拒收。
    /// </para>
    /// <para>
    /// <b>為什麼之前沒被抓到</b>：回呼那一段的既有測試全部走 in-memory 樁
    /// （<c>PaymentOrderingEventHandlerTests</c>、<c>EcpaySimulatorTests</c>），
    /// 沒有一條把回呼的結果真的 <c>SaveChangesAsync</c> 進資料庫。所以這條刻意放在
    /// Migrations.Tests——這裡有真的 Postgres 與真的 <see cref="PaymentRepository"/>。
    /// 用的是<b>真的</b> <see cref="EcpayGateway"/>，通知欄位手動組（跟綠界／模擬器同一個形狀）。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "綠界回呼在真的 Postgres 上寫得進 Captured：台北時間的 PaymentDate 要正規化成 UTC")]
    public async Task Ecpay_callback_captures_the_payment_in_a_real_database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(24, cancellationToken);

        // 台北 2026/09/02 12:00:00 ＝ UTC 2026-09-02T04:00:00Z（同一個瞬間，換算結果不變）。
        var paidAtTaipei = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.FromHours(8));
        var now = paidAtTaipei.ToUniversalTime();
        var orderId = OrderId.New();
        var settings = DevFakeSettings();
        using var httpClient = new HttpClient();
        var gateway = new EcpayGateway(settings, DevFakeHashKey, DevFakeHashIv, httpClient);

        string merchantTradeNo;
        await using (var context = CreatePaymentDbContext(connectionString))
        {
            var initiation = await CreateService(context, gateway, settings, now).InitiateAsync(
                new PaymentInitiationRequest(
                    orderId,
                    Money.OfMajor(100, Currency.TWD),
                    Money.OfMajor(60, Currency.TWD),
                    "callback",
                    new Uri("http://127.0.0.1:5000/v1/webhooks/ecpay"),
                    new Uri("http://127.0.0.1:5002/payment/result?orderId=1")),
                cancellationToken);
            initiation.IsSuccess.ShouldBeTrue();
            merchantTradeNo = initiation.Value.Fields["MerchantTradeNo"];
        }

        var notification = SignedPaymentNotification(merchantTradeNo, paidAtTaipei);
        gateway.VerifyCallback(notification).ShouldBeTrue();

        await using (var context = CreatePaymentDbContext(connectionString))
        {
            // 修法拿掉的話，這一行不是回 Result.Failure，而是直接丟 ArgumentException（Npgsql 拒收）。
            var result = await CreateService(context, gateway, settings, now)
                .HandleEcpayCallbackAsync(notification, cancellationToken);
            result.IsSuccess.ShouldBeTrue();
        }

        await using (var verify = CreatePaymentDbContext(connectionString))
        {
            var payment = await verify.Payments
                .SingleAsync(candidate => candidate.MerchantTradeNo == merchantTradeNo, cancellationToken);

            payment.Status.ShouldBe(PaymentStatus.Captured);
            payment.ProviderTransactionId.ShouldBe(DevFakeTradeNo);
            payment.CapturedAt.ShouldNotBeNull();
            // 同一個瞬間，而且讀回來是 UTC——這才是 timestamptz 存得下的形狀。
            payment.CapturedAt.Value.ShouldBe(now);
            payment.CapturedAt.Value.Offset.ShouldBe(TimeSpan.Zero);
        }
    }

    private async Task<string> CreateMigratedDatabaseAsync(
        int lastMigration,
        CancellationToken cancellationToken)
    {
        var databaseName = $"payment_ledger_{Guid.NewGuid():N}";
        await ExecuteSqlAsync(
            _postgres.GetConnectionString(),
            $"CREATE DATABASE \"{databaseName}\";",
            cancellationToken);
        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName,
        };

        for (var migration = 1; migration <= lastMigration; migration++)
        {
            await ExecuteMigrationAsync(builder.ConnectionString, migration, cancellationToken);
        }

        return builder.ConnectionString;
    }

    private static PaymentDbContext CreatePaymentDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private const string DevFakeMerchantId = "DEVFAKE0000";
    private const string DevFakeHashKey = "DEVFAKEHASHKEY01";
    private const string DevFakeHashIv = "DEVFAKEHASHIV001";
    private const string DevFakeTradeNo = "DEVFAKE2609021200000";

    private static EcpaySettings DevFakeSettings() => new(
        DevFakeMerchantId,
        new Uri("https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5"),
        new Uri("https://payment-stage.ecpay.com.tw/CreditDetail/DoAction"),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(20),
        false);

    private static PaymentApplicationService CreateService(
        PaymentDbContext context,
        IEcpayGateway gateway,
        EcpaySettings settings,
        DateTimeOffset now) =>
        new(new PaymentRepository(context),
            context,
            new NoopPublisher(),
            gateway,
            settings,
            new StubClock(now),
            new StubCorrelationContext());

    /// <summary>
    /// 綠界 AIO「付款結果通知」的欄位，手動組＋用正式碼那一支演算法簽章。
    /// <c>PaymentDate</c> 刻意用台北時間字串——綠界與模擬器送過來的就是這個形狀。
    /// </summary>
    private static Dictionary<string, string> SignedPaymentNotification(
        string merchantTradeNo,
        DateTimeOffset paidAtTaipei)
    {
        var notification = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MerchantID"] = DevFakeMerchantId,
            ["MerchantTradeNo"] = merchantTradeNo,
            ["StoreID"] = string.Empty,
            ["RtnCode"] = "1",
            ["RtnMsg"] = "交易成功",
            ["TradeNo"] = DevFakeTradeNo,
            ["TradeAmt"] = "160",
            ["PaymentDate"] = paidAtTaipei.ToString(
                "yyyy/MM/dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["PaymentType"] = "Credit_CreditCard",
            ["PaymentTypeChargeFee"] = "0",
            ["TradeDate"] = paidAtTaipei.ToString(
                "yyyy/MM/dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["SimulatePaid"] = "0",
        };
        notification["CheckMacValue"] =
            EcpayGateway.ComputeCheckMacValue(notification, DevFakeHashKey, DevFakeHashIv);
        return notification;
    }

    private static EcpaySettings Settings() => new(
        "3002607",
        new Uri("https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5"),
        new Uri("https://payment-stage.ecpay.com.tw/CreditDetail/DoAction"),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(20),
        false);

    private static async Task ExecuteMigrationAsync(
        string connectionString,
        int migration,
        CancellationToken cancellationToken)
    {
        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        var path = Directory.GetFiles(migrations, $"{migration:0000}_*.sql").ShouldHaveSingleItem();
        var sql = string.Join(
            Environment.NewLine,
            File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('\\')));
        await ExecuteSqlAsync(connectionString, sql, cancellationToken);
    }

    private static async Task ExecuteInTransactionAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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

    private static async Task<bool> TryInsertPendingPaymentAsync(
        string connectionString,
        Guid orderId,
        string tradePrefix,
        CancellationToken cancellationToken)
    {
        try
        {
            var merchantTradeNo = $"{tradePrefix}{Guid.NewGuid():N}"[..20];
            await ExecuteSqlAsync(connectionString, $"""
                INSERT INTO payment.payment (
                    id, tenant_id, provider, order_id, status,
                    goods_amount_minor, shipping_amount_minor, merchant_trade_no,
                    created_at, expires_at)
                VALUES (
                    '{Guid.CreateVersion7()}'::uuid, '{TenantId}'::uuid, 1, '{orderId}'::uuid, 0,
                    10000, 6000, '{merchantTradeNo}'::text, now(), now() + interval '30 minutes');
                """, cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            return false;
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
            ?? throw new DirectoryNotFoundException("找不到 GreyGray.slnx。");
    }

    private sealed class StubClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;

        public DateOnly TodayInTaipei => DateOnly.FromDateTime(now.UtcDateTime.AddHours(8));
    }

    private sealed class StubCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "00000000000000000000000000000001";

        public string? CausationId => "0000000000000001";

        public TenantId TenantId => GreyGray.Shared.Kernel.TenantId.Default;
    }

    private sealed class StubGateway : IEcpayGateway
    {
        public IReadOnlyDictionary<string, string> CreateCheckoutFields(
            string merchantTradeNo,
            Money amount,
            string description,
            Uri returnUrl,
            Uri clientBackUrl,
            DateTimeOffset createdAt,
            PaymentMethod method,
            Uri? paymentInfoUrl) => new Dictionary<string, string>();

        public bool VerifyCallback(IReadOnlyDictionary<string, string> fields) => true;

        public Task<EcpayRefundResult> RequestRefundAsync(
            string merchantTradeNo,
            string providerTransactionId,
            Money amount,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("這個樁只涵蓋 migration 測試會用到的路徑，不涉及退款。");
    }

    private sealed class NoopPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent => Task.CompletedTask;
    }
}
