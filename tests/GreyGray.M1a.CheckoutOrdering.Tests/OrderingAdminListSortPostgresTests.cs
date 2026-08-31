using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

/// <summary>
/// BE-29 迴歸：<c>GET /v1/orders</c> 的排序必須真的能翻譯成 SQL。
/// </summary>
/// <remarks>
/// 為什麼要有這一支：<c>OrderingRepository.PageAsync</c> 曾寫成
/// <c>.ThenByDescending(order => order.Id.Value)</c>——把強型別 <see cref="OrderId"/>
/// 用 <c>.Value</c> 拆開再排序，EF Core 的值轉換器翻譯不了，後台訂單列表對真資料庫
/// 一律 500。既有 176 條測試沒有一條抓到，因為 <c>ListAdminAsync</c> 只被
/// fake repository 頂替過，從沒有對真 Postgres 跑過。
/// 所以這裡刻意讓三筆訂單的 <c>PlacedAt</c> 完全相同，逼查詢真的用到 <c>ThenBy</c>
/// 的第二鍵——只塞一筆的話主排序鍵就分得出勝負，第二鍵不會被用到，測試會變成空跑。
/// </remarks>
public sealed class OrderingAdminListSortPostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 4, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 三個刻意寫死的 ID：只差在數值大小，PostgreSQL 的 uuid 排序與 .NET 的
    /// <see cref="Guid.CompareTo(Guid)"/> 對它們的結論一致，斷言才不會依賴
    /// 兩邊比較語意的細節。
    /// </summary>
    private static readonly OrderId Small = new(new Guid("11111111-1111-1111-1111-111111111111"));

    private static readonly OrderId Middle = new(new Guid("22222222-2222-2222-2222-222222222222"));

    private static readonly OrderId Large = new(new Guid("33333333-3333-3333-3333-333333333333"));

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "後台訂單列表：PlacedAt 相同時用 OrderId 遞減 tie-break，且查詢翻譯得成 SQL")]
    public async Task Admin_list_orders_breaks_ties_by_order_id_in_sql()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var dbContext = await CreateDbContextAsync("ordering_admin_sort", cancellationToken);
        await SeedThreeOrdersWithIdenticalPlacedAtAsync(dbContext, cancellationToken);

        var repository = new OrderingRepository(dbContext);
        var page = await repository.ListAdminAsync(
            TenantId.Default,
            new AdminOrderListRequest(null, null, null, null, 20),
            cancellationToken);

        page.Items.Select(order => order.Id).ShouldBe([Large, Middle, Small]);
        page.NextCursor.ShouldBeNull();
    }

    /// <remarks>
    /// BE-29 迴歸：<c>PageAsync</c> 的 <c>Where</c> 子句原本寫
    /// <c>order.Id.Value.CompareTo(cursor) &lt; 0</c>（<c>OrderingRepository.cs:123</c>），
    /// 跟 <c>ThenBy</c> 那一行同一種手法拆開強型別 ID 比較，EF Core 翻譯不成 SQL，
    /// 帶游標的第二頁一律 500。BE-32 改成 <c>order.Id &lt; cursorOrder.Id</c>
    /// （比照 <c>EntryId</c> 的運算子重載）修好，這一條轉綠。
    /// </remarks>
    [Fact(DisplayName = "後台訂單列表 cursor 分頁：PlacedAt 全部相同時第二頁仍接得上，不重複也不漏")]
    public async Task Admin_list_orders_pages_by_cursor_when_placed_at_is_identical()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var dbContext = await CreateDbContextAsync("ordering_admin_cursor", cancellationToken);
        await SeedThreeOrdersWithIdenticalPlacedAtAsync(dbContext, cancellationToken);

        var repository = new OrderingRepository(dbContext);

        // 第一頁：limit 2，拿到最大的兩個，游標停在第二筆。
        var first = await repository.ListAdminAsync(
            TenantId.Default,
            new AdminOrderListRequest(null, null, null, null, 2),
            cancellationToken);
        first.Items.Select(order => order.Id).ShouldBe([Large, Middle]);
        first.NextCursor.ShouldBe(Middle);

        // 第二頁：帶游標。PageAsync 的 Where 子句在這裡才會執行
        // （order.Id.Value.CompareTo(cursor) < 0），第一頁完全走不到。
        var second = await repository.ListAdminAsync(
            TenantId.Default,
            new AdminOrderListRequest(null, null, null, first.NextCursor, 2),
            cancellationToken);
        second.Items.Select(order => order.Id).ShouldBe([Small]);
        second.NextCursor.ShouldBeNull();
    }

    /// <summary>三筆 <c>PlacedAt</c> 一模一樣的訂單，只有 ID 不同——tie-break 只能靠第二排序鍵。</summary>
    private static async Task SeedThreeOrdersWithIdenticalPlacedAtAsync(
        OrderingDbContext dbContext,
        CancellationToken cancellationToken)
    {
        foreach (var id in new[] { Small, Middle, Large })
        {
            dbContext.Orders.Add(PlaceOrder(id));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // 先決條件：三筆的 placed_at 真的相同，否則主排序鍵就分得出勝負，這支測試在空跑。
        (await dbContext.Orders.AsNoTracking()
            .Select(order => order.PlacedAt)
            .Distinct()
            .CountAsync(cancellationToken))
            .ShouldBe(1, "三筆訂單的 PlacedAt 不相同，ThenBy 的第二鍵不會被用到。");
    }

    private static Order PlaceOrder(OrderId id)
    {
        var snapshot = new PricingSnapshot(
            PricingSnapshotId.New(),
            DeliveryMethod.ConvenienceStore,
            400,
            0,
            400,
            Money.OfMajor(60, Currency.TWD),
            FeeRuleSetId.New(),
            FeeRuleId.New(),
            ShippingStrategyKind.Flat,
            ["測試運費"],
            Now);
        var checkout = new CheckoutCompleted(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            CartId.New(),
            CustomerId.New(),
            null,
            DeliveryMethod.ConvenienceStore,
            ShippingPolicy.HoldUntilComplete,
            snapshot.Id,
            [
                new CheckoutLine(
                    SkuId.New(), FulfillmentMode.Preorder,
                    CampaignId.New(), CampaignOfferId.New(), 1,
                    Money.OfMajor(100, Currency.TWD)),
            ],
            $"admin-sort-{id}");

        return Order.Place(id, checkout, snapshot, Now).Value;
    }

    private async Task<OrderingDbContext> CreateDbContextAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        var databaseName = $"{prefix}_{Guid.NewGuid():N}";
        await ExecuteSqlAsync(
            _postgres.GetConnectionString(),
            $"CREATE DATABASE \"{databaseName}\";",
            cancellationToken);
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;

        var dbContext = new OrderingDbContext(new DbContextOptionsBuilder<OrderingDbContext>()
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
