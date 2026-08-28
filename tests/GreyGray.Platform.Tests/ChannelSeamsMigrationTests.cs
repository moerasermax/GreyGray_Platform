using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.Platform.Tests;

/// <summary>以真實 PostgreSQL 鎖住 M0-7 的五個通路擴充接縫。</summary>
public sealed class ChannelSeamsMigrationTests : IAsyncLifetime
{
    private const string DefaultTenantId = "00000000-0000-0000-0000-000000000001";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "0003 可重跑，五個接縫有 owner、權限與資料庫約束")]
    public async Task Channel_seams_are_idempotent_owned_and_enforced()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");

        await ExecuteScriptAsync(Path.Combine(migrations, "0001_schemas_and_roles.sql"), cancellationToken);
        await ExecuteScriptAsync(Path.Combine(migrations, "0002_platform.sql"), cancellationToken);
        await ExecuteScriptAsync(Path.Combine(migrations, "0003_channel_seams.sql"), cancellationToken);

        // CREATE ... IF NOT EXISTS、index 與 seed 都必須允許部署工作重跑。
        await ExecuteScriptAsync(Path.Combine(migrations, "0003_channel_seams.sql"), cancellationToken);

        (await ScalarAsync<int>("""
            SELECT count(*)::int
            FROM pg_tables
            WHERE tableowner = 'greygray_owner'
              AND (schemaname, tablename) IN (
                  ('ordering', 'orders'),
                  ('catalog', 'sku'),
                  ('ledger', 'account'),
                  ('inventory', 'lot'),
                  ('payment', 'payment'));
            """, cancellationToken)).ShouldBe(5);

        (await ScalarAsync<bool>("""
            WITH expected(role_name, table_name) AS (VALUES
                ('greygray_ordering', 'ordering.orders'),
                ('greygray_catalog', 'catalog.sku'),
                ('greygray_ledger', 'ledger.account'),
                ('greygray_inventory', 'inventory.lot'),
                ('greygray_payment', 'payment.payment'))
            SELECT bool_and(has_table_privilege(
                role_name, table_name, 'SELECT,INSERT,UPDATE,DELETE'))
            FROM expected;
            """, cancellationToken)).ShouldBeTrue();

        (await ScalarAsync<bool>("""
            SELECT has_table_privilege('greygray_ordering', 'catalog.sku', 'SELECT');
            """, cancellationToken)).ShouldBeFalse(
                "模組 role 只能讀寫自己的 schema。");

        var orderId = Guid.CreateVersion7();
        await ExecuteSqlAsync(
            $"INSERT INTO ordering.orders (id) VALUES ('{orderId}'::uuid);",
            cancellationToken);
        (await ScalarAsync<string>(
            $"SELECT tenant_id::text || ':' || source_channel::text FROM ordering.orders WHERE id = '{orderId}'::uuid;",
            cancellationToken)).ShouldBe($"{DefaultTenantId}:0");
        await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync(
            $"INSERT INTO ordering.orders (id, source_channel) VALUES ('{Guid.CreateVersion7()}'::uuid, 1);",
            cancellationToken));

        var skuId = Guid.CreateVersion7();
        await ExecuteSqlAsync(
            $"INSERT INTO catalog.sku (id) VALUES ('{skuId}'::uuid);",
            cancellationToken);
        (await ScalarAsync<Guid>(
            $"SELECT id FROM catalog.sku WHERE id = '{skuId}'::uuid;",
            cancellationToken)).ShouldBe(skuId);
        await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync("""
            INSERT INTO catalog.sku (id)
            VALUES ('00000000-0000-0000-0000-000000000000'::uuid);
            """, cancellationToken));

        (await ScalarAsync<string>("""
            SELECT code || ':' || category || ':' || is_active::text
            FROM ledger.account
            WHERE tenant_id = '00000000-0000-0000-0000-000000000001'::uuid
              AND code = '1200';
            """, cancellationToken)).ShouldBe("1200:ASSET:false");

        var lotId = Guid.CreateVersion7();
        await ExecuteSqlAsync($"""
            INSERT INTO inventory.lot (
                id, sku_id, quantity_on_hand, quantity_reserved)
            VALUES ('{lotId}'::uuid, '{skuId}'::uuid, 10, 3);
            """, cancellationToken);
        (await ScalarAsync<string>(
            $"SELECT quantity_channel_allocated::text || ':' || quantity_available::text FROM inventory.lot WHERE id = '{lotId}'::uuid;",
            cancellationToken)).ShouldBe("0:7");
        (await ScalarAsync<string>("""
            SELECT pg_get_expr(adbin, adrelid)
            FROM pg_attrdef
            WHERE adrelid = 'inventory.lot'::regclass
              AND adnum = (
                  SELECT attnum
                  FROM pg_attribute
                  WHERE attrelid = 'inventory.lot'::regclass
                    AND attname = 'quantity_available');
            """, cancellationToken)).ShouldContain("quantity_channel_allocated");
        await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync($"""
            INSERT INTO inventory.lot (
                id, sku_id, quantity_on_hand, quantity_reserved, quantity_channel_allocated)
            VALUES ('{Guid.CreateVersion7()}'::uuid, '{skuId}'::uuid, 10, 3, 1);
            """, cancellationToken));

        await ExecuteSqlAsync($"""
            INSERT INTO payment.payment (id, provider)
            VALUES ('{Guid.CreateVersion7()}'::uuid, 9);
            """, cancellationToken);
        await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync($"""
            INSERT INTO payment.payment (id, provider)
            VALUES ('{Guid.CreateVersion7()}'::uuid, 4);
            """, cancellationToken));

        // 人為放入一張非 owner 建立的表，證明末段斷言不是死文字。
        await ExecuteSqlAsync(
            "CREATE TABLE ordering.owner_assertion_probe (id int);",
            cancellationToken);
        var ownerException = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteScriptAsync(Path.Combine(migrations, "0003_channel_seams.sql"), cancellationToken));
        ownerException.SqlState.ShouldBe("P0001");
        ownerException.MessageText.ShouldContain("owner");
        await ExecuteSqlAsync(
            "DROP TABLE ordering.owner_assertion_probe;",
            cancellationToken);
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

    private async Task ExecuteScriptAsync(string path, CancellationToken cancellationToken)
    {
        var sql = string.Join(
            Environment.NewLine,
            File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('\\')));
        await ExecuteSqlAsync(sql, cancellationToken);
    }

    private async Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30,
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<T> ScalarAsync<T>(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return (T)(value ?? throw new InvalidOperationException("Expected a scalar value."));
    }
}
