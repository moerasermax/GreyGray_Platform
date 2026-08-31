using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Modules.Procurement.Core;
using GreyGray.Modules.Procurement.Infra;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.M1b.Procurement.Tests;

/// <summary>
/// BE-29 迴歸：<c>GET /v1/campaigns/{campaignId}/purchase-items</c> 的排序必須真的能翻譯成 SQL。
/// </summary>
/// <remarks>
/// 為什麼要有這一支：<c>ProcurementRepository.GetCampaignListAsync</c> 曾寫成
/// <c>.ThenBy(item => item.Id.Value)</c>，跟訂單／出貨列表那兩個已經實測回 500 的
/// 手誤一模一樣。這一處在第十四波之前沒有被端到端重現過——開發環境沒有任何團資料，
/// 端點在查詢執行前就先回 <c>procurement.campaign-not-found</c> 404，
/// 根本走不到這一行。既有的 Postgres 測試也沒有一條呼叫過 <c>GetCampaignListAsync</c>。
/// 這支測試補的就是那個缺口：塞真資料、真的執行這條查詢。
/// 三筆的 <c>Status</c> 與 <c>CreatedAt</c>（兩個前置排序鍵）刻意完全相同，
/// 逼查詢真的用到第三鍵 <c>Id</c>。
/// </remarks>
public sealed class ProcurementCampaignListSortPostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 4, 0, 0, TimeSpan.Zero);

    /// <summary>三個刻意寫死的 ID：PostgreSQL uuid 排序與 .NET Guid 比較對它們的結論一致。</summary>
    private static readonly PurchaseItemId Small = new(new Guid("11111111-1111-1111-1111-111111111111"));

    private static readonly PurchaseItemId Middle = new(new Guid("22222222-2222-2222-2222-222222222222"));

    private static readonly PurchaseItemId Large = new(new Guid("33333333-3333-3333-3333-333333333333"));

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "現場採購清單：Status 與 CreatedAt 相同時用 PurchaseItemId 遞增 tie-break，且查詢翻譯得成 SQL")]
    public async Task Campaign_purchase_item_list_breaks_ties_by_id_in_sql()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var campaignId = CampaignId.New();

        foreach (var id in new[] { Large, Small, Middle })
        {
            dbContext.PurchaseItems.Add(PurchaseItemAggregate.Create(
                id,
                TenantId.Default,
                campaignId,
                SkuId.New(),
                OrderLineId.New(),
                2,
                Money.OfMajor(350, Currency.TWD),
                Now).Value);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // 先決條件：前兩個排序鍵真的分不出勝負，否則第三鍵不會被用到，這支測試在空跑。
        var keys = await dbContext.PurchaseItems.AsNoTracking()
            .Select(item => new { item.Status, item.CreatedAt })
            .Distinct()
            .CountAsync(cancellationToken);
        keys.ShouldBe(1, "三筆品項的 Status／CreatedAt 不相同，ThenBy 的第三鍵不會被用到。");

        var repository = new ProcurementRepository(dbContext);
        var items = await repository.GetCampaignListAsync(
            TenantId.Default,
            campaignId,
            tracking: false,
            cancellationToken);

        items.Select(item => item.Id).ShouldBe([Small, Middle, Large]);
    }

    private async Task<ProcurementDbContext> CreateDbContextAsync(CancellationToken cancellationToken)
    {
        var databaseName = $"m1b_procurement_sort_{Guid.NewGuid():N}";
        await ExecuteSqlAsync(
            _postgres.GetConnectionString(),
            $"CREATE DATABASE \"{databaseName}\";",
            cancellationToken);
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;

        var dbContext = new ProcurementDbContext(new DbContextOptionsBuilder<ProcurementDbContext>()
            .UseNpgsql(connectionString)
            .Options);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        return dbContext;
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
