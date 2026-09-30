using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Catalog.Infra;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1a.IdentityCatalog.Tests;

/// <summary>用正式 migration 鏈確認應用層在資料庫 trigger 之前回傳兩層規則的業務錯誤。</summary>
public sealed class CategoryHierarchyMigrationServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await ExecuteMigrationChainAsync(
            _postgres.GetConnectionString(),
            TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "S14：正式 migration 鏈下第三層仍由應用層回 Result，不拋 trigger 例外")]
    public async Task Third_level_is_rejected_as_a_result_before_the_database_trigger()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<ICatalogAdministration>();
        var root = (await admin.CreateCategoryAsync(
            new CategoryInput("根", null, 0), cancellationToken)).Value;
        var child = (await admin.CreateCategoryAsync(
            new CategoryInput("子", null, 0, root.Id), cancellationToken)).Value;

        var result = await admin.CreateCategoryAsync(
            new CategoryInput("孫", null, 0, child.Id), cancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.category-depth-exceeded");
    }

    [Fact(DisplayName = "S15：應用層驗證後的競態由 trigger 擋下並回 Result")]
    public async Task A_database_trigger_conflict_is_returned_as_a_result()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        CategoryView ancestor;
        CategoryView parent;
        CategoryView moving;
        await using (var setupProvider = BuildProvider())
        await using (var setupScope = setupProvider.CreateAsyncScope())
        {
            var setupAdmin = setupScope.ServiceProvider.GetRequiredService<ICatalogAdministration>();
            ancestor = (await setupAdmin.CreateCategoryAsync(
                new CategoryInput("祖", null, 0), cancellationToken)).Value;
            parent = (await setupAdmin.CreateCategoryAsync(
                new CategoryInput("父", null, 1), cancellationToken)).Value;
            moving = (await setupAdmin.CreateCategoryAsync(
                new CategoryInput("待移動", null, 2), cancellationToken)).Value;
        }

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<ICatalogAdministration>();

        await using var raceConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await using var observerConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await raceConnection.OpenAsync(cancellationToken);
        await observerConnection.OpenAsync(cancellationToken);
        await using var raceTransaction = await raceConnection.BeginTransactionAsync(cancellationToken);
        await using (var race = new NpgsqlCommand("""
                         UPDATE catalog.category
                         SET parent_id = @ancestor_id
                         WHERE tenant_id = @tenant_id AND id = @parent_id;
                         """, raceConnection, raceTransaction))
        {
            race.Parameters.AddWithValue("ancestor_id", ancestor.Id.Value);
            race.Parameters.AddWithValue("tenant_id", TenantId.Default.Value);
            race.Parameters.AddWithValue("parent_id", parent.Id.Value);
            (await race.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
        }

        var serviceTask = admin.UpdateCategoryAsync(
            moving.Id,
            new CategoryInput("待移動", null, 2, parent.Id),
            cancellationToken);

        var waitingLocks = 0L;
        var waitTimer = System.Diagnostics.Stopwatch.StartNew();
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

        waitingLocks.ShouldBe(1L, "服務層 SaveChanges 必須真的等待 trigger 的同租戶 advisory lock。");
        serviceTask.IsCompleted.ShouldBeFalse();
        await raceTransaction.CommitAsync(cancellationToken);
        var result = await serviceTask;

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.category-depth-exceeded");
    }

    private ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GreyGray_catalog"] = _postgres.GetConnectionString(),
            })
            .Build();
        return new ServiceCollection()
            .AddSingleton<IClock>(new FakeClock(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero)))
            .AddSingleton<ICorrelationContext>(new FakeCorrelationContext())
            .AddCatalogModule(configuration)
            .BuildServiceProvider(validateScopes: true);
    }

    private static async Task ExecuteMigrationChainAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        foreach (var path in Directory.GetFiles(migrations, "*.sql")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var sql = string.Join(
                Environment.NewLine,
                File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('\\')));
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync(cancellationToken);
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
}
