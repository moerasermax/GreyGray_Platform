using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1a.Migrations.Tests;

/// <summary>
/// BE-54 / ADR-039：0021 在 <c>checkout.cart</c> 與 <c>ordering.orders</c> 各加收件人快照欄位。
/// </summary>
/// <remarks>
/// 「整套 migration 可以重放」是硬性要求（部署會重跑全部），所以這裡跑完 0001～0021 之後
/// 再把 0021 跑一次，欄位不能重複、也不能報錯。
/// </remarks>
public sealed class OrderRecipientMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "0021 以 owner 在 cart 與 orders 各加 recipient_name／phone／address，且可重跑兩次")]
    public async Task Migration_0021_adds_recipient_columns_idempotently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(21, cancellationToken);

        foreach (var (schema, table) in new[] { ("checkout", "cart"), ("ordering", "orders") })
        {
            (await ScalarAsync<string>(connectionString, $"""
                SELECT format('%s/%s', data_type, character_maximum_length)
                FROM information_schema.columns
                WHERE table_schema = '{schema}'
                  AND table_name = '{table}'
                  AND column_name = 'recipient_name';
                """, cancellationToken)).ShouldBe("character varying/50");

            (await ScalarAsync<string>(connectionString, $"""
                SELECT format('%s/%s', data_type, character_maximum_length)
                FROM information_schema.columns
                WHERE table_schema = '{schema}'
                  AND table_name = '{table}'
                  AND column_name = 'recipient_phone';
                """, cancellationToken)).ShouldBe("character varying/20");

            (await ScalarAsync<string>(connectionString, $"""
                SELECT format('%s/%s', data_type, character_maximum_length)
                FROM information_schema.columns
                WHERE table_schema = '{schema}'
                  AND table_name = '{table}'
                  AND column_name = 'recipient_address';
                """, cancellationToken)).ShouldBe("character varying/200");

            // 舊資料沒有這幾個值，所以一定要可空——NOT NULL 會讓 migration 在有資料的正式機上炸。
            (await ScalarAsync<bool>(connectionString, $"""
                SELECT bool_and(is_nullable = 'YES')
                FROM information_schema.columns
                WHERE table_schema = '{schema}'
                  AND table_name = '{table}'
                  AND column_name IN ('recipient_name', 'recipient_phone', 'recipient_address');
                """, cancellationToken)).ShouldBeTrue();

            (await ScalarAsync<string>(connectionString, $"""
                SELECT tableowner FROM pg_tables
                WHERE schemaname = '{schema}' AND tablename = '{table}';
                """, cancellationToken)).ShouldBe("greygray_owner");
        }

        // 可重跑性：0021 再跑一次不能報錯，也不能變出第二組欄位。
        await ExecuteMigrationAsync(connectionString, 21, cancellationToken);

        (await ScalarAsync<long>(connectionString, """
            SELECT count(*)
            FROM information_schema.columns
            WHERE (table_schema, table_name) IN (('checkout', 'cart'), ('ordering', 'orders'))
              AND column_name IN ('recipient_name', 'recipient_phone', 'recipient_address');
            """, cancellationToken)).ShouldBe(6L);
    }

    [Fact(DisplayName = "0021 在『已經套過只有兩欄的舊版』之上重跑，會補上 recipient_address")]
    public async Task Migration_0021_adds_the_address_column_on_top_of_the_earlier_two_column_version()
    {
        // Leader 在 0021 只有兩欄時就已經把它套進 dev 資料庫了。這條精確重現那個狀態：
        // 先跑到 0020，手動做出舊版 0021 的效果（只有 name／phone），再套現在這版。
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(20, cancellationToken);

        await ExecuteSqlAsync(connectionString, """
            SET ROLE greygray_owner;
            ALTER TABLE checkout.cart
                ADD COLUMN IF NOT EXISTS recipient_name varchar(50),
                ADD COLUMN IF NOT EXISTS recipient_phone varchar(20);
            ALTER TABLE ordering.orders
                ADD COLUMN IF NOT EXISTS recipient_name varchar(50),
                ADD COLUMN IF NOT EXISTS recipient_phone varchar(20);
            RESET ROLE;
            """, cancellationToken);

        // 舊資料也要在場：有資料的表加欄位才是正式機的真實情境。
        (await ScalarAsync<long>(connectionString, """
            SELECT count(*)
            FROM information_schema.columns
            WHERE (table_schema, table_name) IN (('checkout', 'cart'), ('ordering', 'orders'))
              AND column_name = 'recipient_address';
            """, cancellationToken)).ShouldBe(0L, "前提：這時候還沒有第三欄。");

        await ExecuteMigrationAsync(connectionString, 21, cancellationToken);

        (await ScalarAsync<long>(connectionString, """
            SELECT count(*)
            FROM information_schema.columns
            WHERE (table_schema, table_name) IN (('checkout', 'cart'), ('ordering', 'orders'))
              AND column_name IN ('recipient_name', 'recipient_phone', 'recipient_address');
            """, cancellationToken)).ShouldBe(6L, "既有兩欄不動，第三欄補上。");

        (await ScalarAsync<string>(connectionString, """
            SELECT tableowner FROM pg_tables
            WHERE schemaname = 'ordering' AND tablename = 'orders';
            """, cancellationToken)).ShouldBe("greygray_owner");
    }

    private async Task<string> CreateMigratedDatabaseAsync(
        int lastMigration,
        CancellationToken cancellationToken)
    {
        var databaseName = $"order_recipient_{Guid.NewGuid():N}";
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
        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        var path = Directory.GetFiles(migrations, $"{migration:0000}_*.sql").ShouldHaveSingleItem();
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
