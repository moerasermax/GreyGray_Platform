using System.Diagnostics;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1a.Migrations.Tests;

/// <summary>ADR-041：0023 分類父子兩層的資料庫守衛、重放與併發證據。</summary>
public sealed class CategoryParentMigrationTests : IAsyncLifetime
{
    private static readonly Guid TenantA = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = new("00000000-0000-0000-0000-000000000002");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "M1：0023 可重放，欄位／約束／索引／trigger／VOLATILE owner 各唯一")]
    public async Task Migration_0023_is_replayable_and_owns_each_database_object()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(23, cancellationToken);

        await ExecuteMigrationAsync(connectionString, 23, cancellationToken);

        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'catalog' AND table_name = 'category' AND column_name = 'parent_id';
            """, cancellationToken)).ShouldBe(1L);
        foreach (var constraint in new[]
                 {
                     "category_parent_same_tenant_fk",
                     "category_parent_not_self",
                 })
        {
            (await ScalarAsync<long>(connectionString, $"""
                SELECT count(*) FROM pg_constraint
                WHERE conrelid = 'catalog.category'::regclass AND conname = '{constraint}';
                """, cancellationToken)).ShouldBe(1L);
        }

        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'catalog' AND tablename = 'category'
              AND indexname = 'ix_category_tenant_parent';
            """, cancellationToken)).ShouldBe(1L);
        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM pg_trigger
            WHERE tgrelid = 'catalog.category'::regclass
              AND tgname = 'category_two_level_guard' AND NOT tgisinternal;
            """, cancellationToken)).ShouldBe(1L);
        (await ScalarAsync<string>(connectionString, """
            SELECT format('%s/%s/%s', pg_get_userbyid(proowner), provolatile, prosecdef)
            FROM pg_proc
            WHERE pronamespace = 'catalog'::regnamespace
              AND proname = 'category_two_level_guard';
            """, cancellationToken)).ShouldBe("greygray_owner/v/f");
        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM pg_proc
            WHERE pronamespace = 'catalog'::regnamespace
              AND proname = 'category_two_level_guard';
            """, cancellationToken)).ShouldBe(1L);
    }

    [Fact(DisplayName = "M2：分類不能指向自己")]
    public async Task A_category_cannot_parent_itself()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(23, cancellationToken);
        var category = Guid.CreateVersion7();
        await InsertCategoryAsync(connectionString, category, TenantA, null, cancellationToken);

        var exception = await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync(
            connectionString,
            $"UPDATE catalog.category SET parent_id = '{category}' WHERE id = '{category}';",
            cancellationToken));

        AssertDatabaseError(exception, "23514", "category_parent_not_self");
    }

    [Fact(DisplayName = "M3：上層分類必須屬於同一租戶")]
    public async Task A_parent_category_from_another_tenant_is_rejected_by_the_foreign_key()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(23, cancellationToken);
        var categoryA = Guid.CreateVersion7();
        var categoryB = Guid.CreateVersion7();
        await InsertCategoryAsync(connectionString, categoryA, TenantA, null, cancellationToken);
        await InsertCategoryAsync(connectionString, categoryB, TenantB, null, cancellationToken);

        var exception = await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync(
            connectionString,
            $"UPDATE catalog.category SET parent_id = '{categoryB}' WHERE id = '{categoryA}';",
            cancellationToken));

        AssertDatabaseError(exception, "23503", "category_parent_same_tenant_fk");
    }

    [Fact(DisplayName = "M4：子分類不能再有子分類")]
    public async Task A_third_level_category_is_rejected_by_the_trigger()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(23, cancellationToken);
        var root = Guid.CreateVersion7();
        var child = Guid.CreateVersion7();
        await InsertCategoryAsync(connectionString, root, TenantA, null, cancellationToken);
        await InsertCategoryAsync(connectionString, child, TenantA, root, cancellationToken);

        var exception = await Should.ThrowAsync<PostgresException>(() => InsertCategoryAsync(
            connectionString,
            Guid.CreateVersion7(),
            TenantA,
            child,
            cancellationToken));

        AssertDatabaseError(exception, "23514", "category_two_level");
    }

    [Fact(DisplayName = "M5：已經有子分類的根分類不能再成為子分類")]
    public async Task A_root_with_children_cannot_become_a_child()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(23, cancellationToken);
        var root = Guid.CreateVersion7();
        var child = Guid.CreateVersion7();
        var otherRoot = Guid.CreateVersion7();
        await InsertCategoryAsync(connectionString, root, TenantA, null, cancellationToken);
        await InsertCategoryAsync(connectionString, child, TenantA, root, cancellationToken);
        await InsertCategoryAsync(connectionString, otherRoot, TenantA, null, cancellationToken);

        var exception = await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync(
            connectionString,
            $"UPDATE catalog.category SET parent_id = '{otherRoot}' WHERE id = '{root}';",
            cancellationToken));

        AssertDatabaseError(exception, "23514", "category_two_level");
    }

    [Fact(DisplayName = "M6：greygray_catalog 可通過 SECURITY INVOKER trigger 建立子分類")]
    public async Task Catalog_role_can_insert_a_child_through_the_trigger()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(23, cancellationToken);
        var root = Guid.CreateVersion7();
        var child = Guid.CreateVersion7();
        await InsertCategoryAsync(connectionString, root, TenantA, null, cancellationToken);

        await ExecuteSqlAsync(connectionString, $"""
            SET ROLE greygray_catalog;
            INSERT INTO catalog.category (id, tenant_id, name, sort_order, parent_id)
            VALUES ('{child}', '{TenantA}', '子', 1, '{root}');
            RESET ROLE;
            """, cancellationToken);

        (await ScalarAsync<Guid>(connectionString, $"""
            SELECT parent_id FROM catalog.category WHERE id = '{child}';
            """, cancellationToken)).ShouldBe(root);
    }

    [Fact(DisplayName = "M7：互設父子的第二筆交易先等待 advisory lock，再看見第一筆並失敗")]
    public async Task Concurrent_inverse_parent_updates_are_serialized_and_one_is_rolled_back()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;
        var connectionString = await CreateMigratedDatabaseAsync(23, cancellationToken);
        var categoryA = Guid.CreateVersion7();
        var categoryB = Guid.CreateVersion7();
        await InsertCategoryAsync(connectionString, categoryA, TenantA, null, cancellationToken);
        await InsertCategoryAsync(connectionString, categoryB, TenantA, null, cancellationToken);

        await using var firstConnection = new NpgsqlConnection(connectionString);
        await using var secondConnection = new NpgsqlConnection(connectionString);
        await using var observerConnection = new NpgsqlConnection(connectionString);
        await firstConnection.OpenAsync(cancellationToken);
        await secondConnection.OpenAsync(cancellationToken);
        await observerConnection.OpenAsync(cancellationToken);
        await using var firstTransaction = await firstConnection.BeginTransactionAsync(cancellationToken);
        await using var secondTransaction = await secondConnection.BeginTransactionAsync(cancellationToken);

        await using (var firstUpdate = new NpgsqlCommand(
                         $"UPDATE catalog.category SET parent_id = '{categoryB}' WHERE id = '{categoryA}';",
                         firstConnection,
                         firstTransaction))
        {
            await firstUpdate.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var secondUpdate = new NpgsqlCommand(
            $"UPDATE catalog.category SET parent_id = '{categoryA}' WHERE id = '{categoryB}';",
            secondConnection,
            secondTransaction)
        {
            CommandTimeout = 25,
        };
        var secondUpdateTask = secondUpdate.ExecuteNonQueryAsync(cancellationToken);

        var waitingLocks = 0L;
        var waitTimer = Stopwatch.StartNew();
        while (waitTimer.Elapsed < TimeSpan.FromSeconds(10) && waitingLocks == 0)
        {
            await Task.Delay(50, cancellationToken);
            await using var observe = new NpgsqlCommand("""
                SELECT count(*)
                FROM pg_locks
                WHERE locktype = 'advisory'
                  AND NOT granted
                  AND database = (SELECT oid FROM pg_database WHERE datname = current_database());
                """, observerConnection);
            waitingLocks = (long)(await observe.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("pg_locks 沒有回傳 count。"));
        }

        waitingLocks.ShouldBe(1L, "第二條交易必須真的等待同租戶 advisory lock。");
        secondUpdateTask.IsCompleted.ShouldBeFalse();
        await firstTransaction.CommitAsync(cancellationToken);

        var exception = await Should.ThrowAsync<PostgresException>(async () =>
            await secondUpdateTask);
        AssertDatabaseError(exception, "23514", "category_two_level");
        await secondTransaction.RollbackAsync(cancellationToken);

        var rows = await QueryParentsAsync(connectionString, categoryA, categoryB, cancellationToken);
        rows[categoryA].ShouldBe(categoryB);
        rows[categoryB].ShouldBeNull();
    }

    [Fact(DisplayName = "M8：0023 套在既有分類與商品上，分類成為根且商品不變")]
    public async Task Existing_categories_and_products_survive_migration_0023()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateMigratedDatabaseAsync(22, cancellationToken);
        var firstCategory = Guid.CreateVersion7();
        var secondCategory = Guid.CreateVersion7();
        var product = Guid.CreateVersion7();
        await InsertCategoryAsync(connectionString, firstCategory, TenantA, null, cancellationToken);
        await InsertCategoryAsync(connectionString, secondCategory, TenantA, null, cancellationToken);
        await ExecuteSqlAsync(connectionString, $"""
            INSERT INTO catalog.product (
                id, tenant_id, name, category_id, mode, is_active, created_at)
            VALUES ('{product}', '{TenantA}', '既有商品', '{firstCategory}', 0, true, now());
            """, cancellationToken);

        await ExecuteMigrationAsync(connectionString, 23, cancellationToken);

        (await ScalarAsync<long>(connectionString, """
            SELECT count(*) FROM catalog.category WHERE parent_id IS NULL;
            """, cancellationToken)).ShouldBe(2L);
        (await ScalarAsync<Guid>(connectionString, $"""
            SELECT category_id FROM catalog.product WHERE id = '{product}';
            """, cancellationToken)).ShouldBe(firstCategory);
        (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM catalog.product;", cancellationToken))
            .ShouldBe(1L);
    }

    private async Task<string> CreateMigratedDatabaseAsync(
        int lastMigration,
        CancellationToken cancellationToken)
    {
        var databaseName = $"category_parent_{Guid.NewGuid():N}";
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
        var directory = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        var path = Directory.GetFiles(directory, $"{migration:0000}_*.sql").ShouldHaveSingleItem();
        var sql = string.Join(
            Environment.NewLine,
            File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('\\')));
        await ExecuteSqlAsync(connectionString, sql, cancellationToken);
    }

    private static async Task InsertCategoryAsync(
        string connectionString,
        Guid id,
        Guid tenantId,
        Guid? parentId,
        CancellationToken cancellationToken) =>
        await ExecuteSqlAsync(connectionString, $"""
            INSERT INTO catalog.category (id, tenant_id, name, sort_order{(parentId.HasValue ? ", parent_id" : string.Empty)})
            VALUES ('{id}', '{tenantId}', '分類 {id:N}', 0{(parentId.HasValue ? $", '{parentId.Value}'" : string.Empty)});
            """, cancellationToken);

    private static async Task<Dictionary<Guid, Guid?>> QueryParentsAsync(
        string connectionString,
        Guid first,
        Guid second,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Guid?>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT id, parent_id FROM catalog.category WHERE id IN ('{first}', '{second}');
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetGuid(0)] = reader.IsDBNull(1) ? null : reader.GetGuid(1);
        }

        return result;
    }

    private static void AssertDatabaseError(
        PostgresException exception,
        string sqlState,
        string constraintName)
    {
        exception.SqlState.ShouldBe(sqlState);
        exception.ConstraintName.ShouldBe(constraintName);
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
