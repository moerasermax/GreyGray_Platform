using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Fulfillment.Core;
using GreyGray.Modules.Fulfillment.Infra;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1b.Fulfillment.Tests;

/// <summary>
/// BE-29 迴歸：<c>GET /v1/shipments</c> 的排序必須真的能翻譯成 SQL。
/// </summary>
/// <remarks>
/// 為什麼要有這一支：<c>FulfillmentRepository.ListAsync</c> 曾寫成
/// <c>.ThenByDescending(shipment => shipment.Id.Value)</c>——把強型別
/// <see cref="ShipmentId"/> 用 <c>.Value</c> 拆開再排序，EF Core 的值轉換器翻譯不了，
/// 後台出貨列表對真資料庫一律 500。既有測試沒有一條呼叫過 <c>ListAsync</c>，
/// 所以這個缺口活到了第十四波。
/// 三筆的 <c>CreatedAt</c> 刻意完全相同，逼查詢真的用到 <c>ThenBy</c> 的第二鍵。
/// </remarks>
public sealed class FulfillmentAdminListSortPostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 4, 0, 0, TimeSpan.Zero);

    /// <summary>三個刻意寫死的 ID：PostgreSQL uuid 排序與 .NET Guid 比較對它們的結論一致。</summary>
    private static readonly ShipmentId Small = new(new Guid("11111111-1111-1111-1111-111111111111"));

    private static readonly ShipmentId Middle = new(new Guid("22222222-2222-2222-2222-222222222222"));

    private static readonly ShipmentId Large = new(new Guid("33333333-3333-3333-3333-333333333333"));

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "後台出貨列表：CreatedAt 相同時用 ShipmentId 遞減 tie-break，且查詢翻譯得成 SQL")]
    public async Task Admin_list_shipments_breaks_ties_by_shipment_id_in_sql()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        await SeedThreeShipmentsWithIdenticalCreatedAtAsync(dbContext, cancellationToken);

        var repository = new FulfillmentRepository(dbContext);
        var page = await repository.ListAsync(
            TenantId.Default,
            new AdminShipmentListRequest(null, null, 20),
            cancellationToken);

        page.Items.Select(shipment => shipment.Id).ShouldBe([Large, Middle, Small]);
        page.NextCursor.ShouldBeNull();
    }

    /// <remarks>
    /// BE-29 迴歸：跟 <c>OrderingAdminListSortPostgresTests</c> 那一條同源——
    /// <c>ListAsync</c> 的 <c>Where</c> 子句原本寫
    /// <c>shipment.Id.Value.CompareTo(cursor) &lt; 0</c>（<c>FulfillmentRepository.cs:61</c>），
    /// EF Core 翻譯不成 SQL，帶游標的第二頁一律 500。BE-32 改成
    /// <c>shipment.Id &lt; cursorShipment.Id</c>（比照 <c>EntryId</c> 的運算子重載）修好，
    /// 這一條轉綠。
    /// </remarks>
    [Fact(DisplayName = "後台出貨列表 cursor 分頁：CreatedAt 全部相同時第二頁仍接得上，不重複也不漏")]
    public async Task Admin_list_shipments_pages_by_cursor_when_created_at_is_identical()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        await SeedThreeShipmentsWithIdenticalCreatedAtAsync(dbContext, cancellationToken);

        var repository = new FulfillmentRepository(dbContext);

        var first = await repository.ListAsync(
            TenantId.Default,
            new AdminShipmentListRequest(null, null, 2),
            cancellationToken);
        first.Items.Select(shipment => shipment.Id).ShouldBe([Large, Middle]);
        first.NextCursor.ShouldBe(Middle);

        // 帶游標的第二頁才會執行 Where 裡的 Id.Value.CompareTo(...)。
        var second = await repository.ListAsync(
            TenantId.Default,
            new AdminShipmentListRequest(null, first.NextCursor, 2),
            cancellationToken);
        second.Items.Select(shipment => shipment.Id).ShouldBe([Small]);
        second.NextCursor.ShouldBeNull();
    }

    /// <summary>三筆 <c>CreatedAt</c> 一模一樣的出貨單，只有 ID 不同。</summary>
    private static async Task SeedThreeShipmentsWithIdenticalCreatedAtAsync(
        FulfillmentDbContext dbContext,
        CancellationToken cancellationToken)
    {
        foreach (var id in new[] { Small, Middle, Large })
        {
            dbContext.Shipments.Add(ShipmentAggregate.Create(
                id,
                TenantId.Default,
                DeliveryMethod.HomeDelivery,
                [OrderId.New()],
                Now).Value);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // 先決條件：三筆的 created_at 真的相同，否則主排序鍵就分得出勝負，這支測試在空跑。
        (await dbContext.Shipments.AsNoTracking()
            .Select(shipment => shipment.CreatedAt)
            .Distinct()
            .CountAsync(cancellationToken))
            .ShouldBe(1, "三筆出貨單的 CreatedAt 不相同，ThenBy 的第二鍵不會被用到。");
    }

    private async Task<FulfillmentDbContext> CreateDbContextAsync(CancellationToken cancellationToken)
    {
        var databaseName = $"m1b4_shipment_sort_{Guid.NewGuid():N}";
        await ExecuteSqlAsync(
            _postgres.GetConnectionString(),
            $"CREATE DATABASE \"{databaseName}\";",
            cancellationToken);
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;

        var migrations = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        foreach (var name in new[]
                 {
                     "0001_schemas_and_roles.sql",
                     "0002_platform.sql",
                     "0012_m1b_fulfillment.sql",
                 })
        {
            await ExecuteScriptAsync(connectionString, Path.Combine(migrations, name), cancellationToken);
        }

        return new FulfillmentDbContext(new DbContextOptionsBuilder<FulfillmentDbContext>()
            .UseNpgsql(connectionString)
            .Options);
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
}
