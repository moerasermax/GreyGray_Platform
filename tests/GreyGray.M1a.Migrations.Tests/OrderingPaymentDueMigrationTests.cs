using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1a.Migrations.Tests;

public sealed class OrderingPaymentDueMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "0001～0025 整套重跑兩次：各狀態期限與 timer 筆數、fire_at 都不變")]
    public async Task Migration_0025_backfills_and_schedules_idempotently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(24, cancellationToken);
        await ExecuteSqlAsync(connectionString, SeedOrdersSql, cancellationToken);

        var t0 = await ScalarAsync<DateTime>(
            connectionString,
            "SELECT clock_timestamp();",
            cancellationToken);
        await ExecuteMigrationAsync(connectionString, 25, cancellationToken);
        await ExecuteSqlAsync(connectionString, NewOrderSql, cancellationToken);

        (await ScalarAsync<string>(connectionString, """
            SELECT format('%s/%s', data_type, is_nullable)
            FROM information_schema.columns
            WHERE table_schema = 'ordering' AND table_name = 'orders'
              AND column_name = 'payment_auto_cancel_at';
            """, cancellationToken)).ShouldBe("timestamp with time zone/YES");
        (await ScalarAsync<string>(connectionString, """
            SELECT format('%s/%s', data_type, is_nullable)
            FROM information_schema.columns
            WHERE table_schema = 'ordering' AND table_name = 'orders'
              AND column_name = 'cancellation_source';
            """, cancellationToken)).ShouldBe("smallint/YES");

        (await ScalarAsync<bool>(connectionString, """
            SELECT payment_due_at = placed_at + interval '24 hours'
               AND payment_auto_cancel_at = placed_at + interval '24 hours'
            FROM ordering.orders
            WHERE order_number = 'GG-M25-WAITING';
            """, cancellationToken)).ShouldBeTrue();
        (await ScalarAsync<bool>(connectionString, """
            SELECT payment_due_at IS NULL AND payment_auto_cancel_at IS NULL
            FROM ordering.orders
            WHERE order_number = 'GG-M25-PAID';
            """, cancellationToken)).ShouldBeTrue();
        (await ScalarAsync<bool>(connectionString, """
            SELECT payment_due_at = placed_at + interval '12 hours'
               AND payment_auto_cancel_at = payment_due_at
            FROM ordering.orders
            WHERE order_number = 'GG-M25-EXISTING';
            """, cancellationToken)).ShouldBeTrue();
        (await ScalarAsync<bool>(connectionString, """
            SELECT payment_due_at = placed_at + interval '8 hours'
               AND payment_auto_cancel_at IS NULL
            FROM ordering.orders
            WHERE order_number = 'GG-M25-CANCELLED';
            """, cancellationToken)).ShouldBeTrue();
        (await ScalarAsync<bool>(connectionString, """
            SELECT payment_due_at = placed_at + interval '10 hours'
               AND payment_auto_cancel_at IS NULL
            FROM ordering.orders
            WHERE order_number = 'GG-M25-COMPLETED';
            """, cancellationToken)).ShouldBeTrue();
        (await ScalarAsync<bool>(connectionString, """
            SELECT payment_due_at = placed_at + interval '6 hours'
               AND payment_auto_cancel_at = payment_due_at + interval '2 days'
            FROM ordering.orders
            WHERE order_number = 'GG-M25-NEW';
            """, cancellationToken)).ShouldBeTrue();

        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM platform.saga_timer
            WHERE saga_type = 'ordering.payment-due';
            """, cancellationToken)).ShouldBe(3L);
        (await ScalarAsync<string>(connectionString, """
            SELECT saga_id FROM platform.saga_timer
            WHERE saga_type = 'ordering.payment-due'
              AND saga_id = '11111111111111111111111111111111';
            """, cancellationToken)).ShouldBe("11111111111111111111111111111111");
        var fireAt = await ScalarAsync<DateTime>(connectionString, """
                SELECT fire_at FROM platform.saga_timer
                WHERE saga_type = 'ordering.payment-due'
                  AND saga_id = '11111111111111111111111111111111';
                """, cancellationToken);
        fireAt.ShouldBeGreaterThanOrEqualTo(t0 + TimeSpan.FromHours(1));
        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM platform.saga_timer
            WHERE saga_type = 'ordering.payment-due'
              AND saga_id IN (
                  '14444444444444444444444444444444',
                  '15555555555555555555555555555555');
            """, cancellationToken)).ShouldBe(0L);
        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM platform.saga_timer
            WHERE saga_type = 'ordering.payment-due'
              AND saga_id = '16666666666666666666666666666666';
            """, cancellationToken)).ShouldBe(1L);

        var timerCountAfterFirstChain = await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM platform.saga_timer
            WHERE saga_type = 'ordering.payment-due';
            """, cancellationToken);
        var deadlineAfterFirstChain = await ScalarAsync<DateTime>(connectionString, """
            SELECT payment_auto_cancel_at FROM ordering.orders
            WHERE order_number = 'GG-M25-WAITING';
            """, cancellationToken);
        var fireAtAfterFirstChain = await ScalarAsync<DateTime>(connectionString, """
            SELECT fire_at FROM platform.saga_timer
            WHERE saga_type = 'ordering.payment-due'
              AND saga_id = '11111111111111111111111111111111';
            """, cancellationToken);

        for (var replay = 0; replay < 2; replay++)
        {
            for (var migration = 1; migration <= 25; migration++)
            {
                await ExecuteMigrationAsync(connectionString, migration, cancellationToken);
            }
        }

        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM platform.saga_timer
            WHERE saga_type = 'ordering.payment-due';
            """, cancellationToken)).ShouldBe(timerCountAfterFirstChain);
        (await ScalarAsync<DateTime>(connectionString, """
            SELECT payment_auto_cancel_at FROM ordering.orders
            WHERE order_number = 'GG-M25-WAITING';
            """, cancellationToken)).ShouldBe(deadlineAfterFirstChain);
        (await ScalarAsync<DateTime>(connectionString, """
            SELECT fire_at FROM platform.saga_timer
            WHERE saga_type = 'ordering.payment-due'
              AND saga_id = '11111111111111111111111111111111';
            """, cancellationToken)).ShouldBe(fireAtAfterFirstChain);
        (await ScalarAsync<bool>(connectionString, """
            SELECT bool_and(timer_count = expected_timer_count)
            FROM (
                SELECT order_number,
                       CASE order_number WHEN 'GG-M25-NEW' THEN 1 ELSE 0 END AS expected_timer_count,
                       (SELECT count(*)
                        FROM platform.saga_timer timer
                        WHERE timer.saga_type = 'ordering.payment-due'
                          AND timer.saga_id = replace(orders.id::text, '-', '')) AS timer_count
                FROM ordering.orders orders
                WHERE order_number IN ('GG-M25-CANCELLED', 'GG-M25-COMPLETED', 'GG-M25-NEW')
            ) seeded;
            """, cancellationToken)).ShouldBeTrue();
        (await ScalarAsync<bool>(connectionString, """
            SELECT bool_and(deadline_unchanged)
            FROM (
                SELECT order_number,
                       CASE order_number
                           WHEN 'GG-M25-CANCELLED' THEN
                               payment_due_at = placed_at + interval '8 hours'
                               AND payment_auto_cancel_at IS NULL
                           WHEN 'GG-M25-COMPLETED' THEN
                               payment_due_at = placed_at + interval '10 hours'
                               AND payment_auto_cancel_at IS NULL
                           WHEN 'GG-M25-NEW' THEN
                               payment_due_at = placed_at + interval '6 hours'
                               AND payment_auto_cancel_at = payment_due_at + interval '2 days'
                       END AS deadline_unchanged
                FROM ordering.orders
                WHERE order_number IN ('GG-M25-CANCELLED', 'GG-M25-COMPLETED', 'GG-M25-NEW')
            ) seeded;
            """, cancellationToken)).ShouldBeTrue();

        var invalidSource = await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync(
            connectionString,
            "UPDATE ordering.orders SET cancellation_source = 4 WHERE order_number = 'GG-M25-WAITING';",
            cancellationToken));
        invalidSource.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact(DisplayName = "0025 回填語句是 UPDATE RETURNING 接 INSERT，沒有 JOIN")]
    public void Migration_0025_keeps_the_required_single_statement_shape()
    {
        var path = MigrationPath(25);
        var sql = File.ReadAllText(path);

        sql.ShouldContain("WITH b AS (");
        sql.ShouldContain("UPDATE ordering.orders");
        sql.ShouldContain("payment_due_at = COALESCE(payment_due_at, placed_at + interval '24 hours')");
        sql.ShouldContain("payment_auto_cancel_at = COALESCE(payment_due_at, placed_at + interval '24 hours')");
        sql.ShouldContain("WHERE status = 0");
        sql.ShouldContain("AND payment_auto_cancel_at IS NULL");
        sql.ShouldContain("RETURNING id, tenant_id, payment_auto_cancel_at");
        sql.ShouldContain("INSERT INTO platform.saga_timer");
        sql.ShouldContain("replace(id::text, '-', '')");
        sql.ShouldNotContain(" JOIN ", Case.Insensitive);
    }

    private async Task<string> CreateMigratedDatabaseAsync(
        int lastMigration,
        CancellationToken cancellationToken)
    {
        var databaseName = $"ordering_due_{Guid.NewGuid():N}";
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

    private static async Task ExecuteMigrationAsync(
        string connectionString,
        int migration,
        CancellationToken cancellationToken)
    {
        var sql = string.Join(
            Environment.NewLine,
            File.ReadLines(MigrationPath(migration))
                .Where(line => !line.TrimStart().StartsWith('\\')));
        await ExecuteSqlAsync(connectionString, sql, cancellationToken);
    }

    private static string MigrationPath(int migration)
    {
        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        return Directory.GetFiles(migrations, $"{migration:0000}_*.sql").ShouldHaveSingleItem();
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

    private const string SeedOrdersSql = """
        SET ROLE greygray_owner;
        INSERT INTO ordering.orders (
            id, tenant_id, source_channel, checkout_event_id, checkout_cart_id,
            checkout_idempotency_key, order_number, customer_id, status,
            shipping_policy, delivery_method, pricing_snapshot_id,
            goods_total_amount_minor, goods_total_currency,
            shipping_fee_amount_minor, shipping_fee_currency,
            grand_total_amount_minor, grand_total_currency,
            refunded_amount_minor, quote_explain, placed_at, payment_due_at)
        VALUES
            ('11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000001', 0,
             '21111111-1111-1111-1111-111111111111', '31111111-1111-1111-1111-111111111111',
             'm25-waiting', 'GG-M25-WAITING', '41111111-1111-1111-1111-111111111111', 0,
             1, 1, '51111111-1111-1111-1111-111111111111',
             10000, 'TWD', 6000, 'TWD', 16000, 'TWD', 0, '[]'::jsonb,
             '2026-09-01T00:00:00Z', NULL),
            ('12222222-2222-2222-2222-222222222222', '00000000-0000-0000-0000-000000000001', 0,
             '22222222-2222-2222-2222-222222222222', '32222222-2222-2222-2222-222222222222',
             'm25-paid', 'GG-M25-PAID', '42222222-2222-2222-2222-222222222222', 1,
             1, 1, '52222222-2222-2222-2222-222222222222',
             10000, 'TWD', 6000, 'TWD', 16000, 'TWD', 0, '[]'::jsonb,
             '2026-09-01T00:00:00Z', NULL),
            ('13333333-3333-3333-3333-333333333333', '00000000-0000-0000-0000-000000000001', 0,
             '23333333-3333-3333-3333-333333333333', '33333333-3333-3333-3333-333333333334',
             'm25-existing', 'GG-M25-EXISTING', '43333333-3333-3333-3333-333333333333', 0,
             1, 1, '53333333-3333-3333-3333-333333333333',
             10000, 'TWD', 6000, 'TWD', 16000, 'TWD', 0, '[]'::jsonb,
             '2026-09-01T00:00:00Z', '2026-09-01T12:00:00Z'),
            ('14444444-4444-4444-4444-444444444444', '00000000-0000-0000-0000-000000000001', 0,
             '24444444-4444-4444-4444-444444444444', '34444444-4444-4444-4444-444444444444',
             'm25-cancelled', 'GG-M25-CANCELLED', '44444444-4444-4444-4444-444444444444', 9,
             1, 1, '54444444-4444-4444-4444-444444444444',
             10000, 'TWD', 6000, 'TWD', 16000, 'TWD', 0, '[]'::jsonb,
             '2026-09-01T00:00:00Z', '2026-09-01T08:00:00Z'),
            ('15555555-5555-5555-5555-555555555555', '00000000-0000-0000-0000-000000000001', 0,
             '25555555-5555-5555-5555-555555555555', '35555555-5555-5555-5555-555555555555',
             'm25-completed', 'GG-M25-COMPLETED', '45555555-5555-5555-5555-555555555555', 7,
             1, 1, '55555555-5555-5555-5555-555555555556',
             10000, 'TWD', 6000, 'TWD', 16000, 'TWD', 0, '[]'::jsonb,
             '2026-09-01T00:00:00Z', '2026-09-01T10:00:00Z');
        RESET ROLE;
        """;

    private const string NewOrderSql = """
        SET ROLE greygray_owner;
        INSERT INTO ordering.orders (
            id, tenant_id, source_channel, checkout_event_id, checkout_cart_id,
            checkout_idempotency_key, order_number, customer_id, status,
            shipping_policy, delivery_method, pricing_snapshot_id,
            goods_total_amount_minor, goods_total_currency,
            shipping_fee_amount_minor, shipping_fee_currency,
            grand_total_amount_minor, grand_total_currency,
            refunded_amount_minor, quote_explain, placed_at,
            payment_due_at, payment_auto_cancel_at)
        VALUES (
            '16666666-6666-6666-6666-666666666666', '00000000-0000-0000-0000-000000000001', 0,
            '26666666-6666-6666-6666-666666666666', '36666666-6666-6666-6666-666666666666',
            'm25-new', 'GG-M25-NEW', '46666666-6666-6666-6666-666666666666', 0,
            1, 1, '56666666-6666-6666-6666-666666666666',
            10000, 'TWD', 6000, 'TWD', 16000, 'TWD', 0, '[]'::jsonb,
            '2026-09-01T00:00:00Z', '2026-09-01T06:00:00Z', '2026-09-03T06:00:00Z');
        INSERT INTO platform.saga_timer
            (id, tenant_id, saga_type, saga_id, fire_at, payload, created_at)
        VALUES (
            '66666666-6666-6666-6666-666666666667',
            '00000000-0000-0000-0000-000000000001',
            'ordering.payment-due',
            '16666666666666666666666666666666',
            '2026-09-03T06:00:00Z',
            '{}'::jsonb,
            '2026-09-01T00:00:00Z');
        RESET ROLE;
        """;
}
