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

public sealed class PaymentLedgerMigrationTests : IAsyncLifetime
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
        var connectionString = await CreateMigratedDatabaseAsync(8, cancellationToken);
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
                    new Uri("https://example.test/payment-return")),
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

    private static EcpaySettings Settings() => new(
        "3002607",
        new Uri("https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5"),
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
            DateTimeOffset createdAt) => new Dictionary<string, string>();

        public bool VerifyCallback(IReadOnlyDictionary<string, string> fields) => true;
    }

    private sealed class NoopPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent => Task.CompletedTask;
    }
}
