using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1a.Migrations.Tests;

public sealed class OrderingAppraisalMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "0013 以 owner 加 appraisal_due_at 欄位與守衛 constraint，且可重跑兩次")]
    public async Task Migration_0013_adds_appraisal_column_idempotently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(13, cancellationToken);

        (await ScalarAsync<bool>(connectionString, """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'ordering'
                  AND table_name = 'orders'
                  AND column_name = 'appraisal_due_at'
                  AND data_type = 'timestamp with time zone');
            """, cancellationToken)).ShouldBeTrue();

        (await ScalarAsync<bool>(connectionString, """
            SELECT EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conrelid = 'ordering.orders'::regclass
                  AND conname = 'orders_appraisal_due_requires_shipped');
            """, cancellationToken)).ShouldBeTrue();

        (await ScalarAsync<string>(connectionString, """
            SELECT tableowner FROM pg_tables
            WHERE schemaname = 'ordering' AND tablename = 'orders';
            """, cancellationToken)).ShouldBe("greygray_owner");

        // 可重跑性：0013 再跑一次不能報錯，也不能重複建立 constraint。
        await ExecuteMigrationAsync(connectionString, 13, cancellationToken);

        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM pg_constraint
            WHERE conrelid = 'ordering.orders'::regclass
              AND conname = 'orders_appraisal_due_requires_shipped';
            """, cancellationToken)).ShouldBe(1L);
    }

    private async Task<string> CreateMigratedDatabaseAsync(
        int lastMigration,
        CancellationToken cancellationToken)
    {
        var databaseName = $"ordering_appraisal_{Guid.NewGuid():N}";
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
