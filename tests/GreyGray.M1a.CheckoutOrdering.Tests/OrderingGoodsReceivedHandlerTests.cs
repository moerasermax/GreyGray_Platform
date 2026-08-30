using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Observability;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

/// <summary>
/// BE-21：第六波驗收發現「帶回入庫後訂單轉待出貨」這條線是斷的——
/// <c>IOrderingGoodsReceipt.RecordGoodsReceivedAsync</c> 已存在、已 DI 註冊，但沒有任何呼叫點。
/// 這裡不直接呼叫 port，而是把真正的 <c>GoodsReceived</c> 事件送進
/// <c>Ordering.Infra</c> 用 <c>AddIdempotentIntegrationEventHandler</c> 註冊的 handler，
/// 證明接線本身（DI 註冊 ＋ processed_message 冪等 ＋ outbox）是通的。
/// </summary>
/// <remarks>
/// 用 <c>EnsureCreatedAsync</c>（EF model 產生 schema）而不是套用 <c>db/migrations</c> 的原始 SQL——
/// 原因見 <c>.dispatch/reports/BE-21.md</c>「我發現但沒做的事」：套真正的 0006 migration 會讓
/// <c>orders_refunded_consistent</c> check constraint 擋下每一筆完成付款的訂單，
/// 因為 <c>Order.CapturePayment</c>（<c>Ordering.Core</c>，不在 BE-21 的 allow）
/// 在還沒有任何退款時就把 <c>RefundedCurrency</c> 設成非 null。
/// </remarks>
public sealed class OrderingGoodsReceivedHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await using var dbContext = NewDbContext();
        await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName =
        "GoodsReceived 經 Ordering.Infra 的 handler：兩條預購 line 只帶回一條時不轉待出貨，" +
        "最後一條帶回才轉 ReadyToShip 且只發一次 OrderReadyToShip")]
    public async Task GoodsReceived_only_transitions_order_when_last_preorder_line_arrives()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);

        OrderId orderId;
        OrderLineId firstLineId;
        OrderLineId lastLineId;
        var campaignId = CampaignId.New();
        var skuId = SkuId.New();

        await using (var seedContext = NewDbContext())
        {
            var order = CreatePaidAndPurchasedOrder(preorderCount: 2, campaignId, skuId);
            orderId = order.Id;
            firstLineId = order.Lines[0].Id;
            lastLineId = order.Lines[1].Id;
            seedContext.Orders.Add(order);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        await using var provider = BuildProvider();

        var firstReceived = new GoodsReceived(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            campaignId,
            skuId,
            1,
            Money.OfMajor(100, Currency.TWD),
            LotSource.OverseasPurchase,
            firstLineId);

        await DispatchAsync(provider, firstReceived, cancellationToken);

        // 冪等：重送同一條事件不能讓狀態或 outbox 動第二次。
        await DispatchAsync(provider, firstReceived, cancellationToken);

        (await ReadOrderStatusAsync(orderId, cancellationToken)).ShouldBe(OrderStatus.Purchasing);
        (await CountOutboxAsync(OrderReadyToShip.EventType, cancellationToken)).ShouldBe(0);
        (await CountProcessedAsync(firstReceived.EventId, cancellationToken)).ShouldBe(1);

        var lastReceived = new GoodsReceived(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            campaignId,
            skuId,
            1,
            Money.OfMajor(100, Currency.TWD),
            LotSource.OverseasPurchase,
            lastLineId);

        await DispatchAsync(provider, lastReceived, cancellationToken);
        await DispatchAsync(provider, lastReceived, cancellationToken);

        (await ReadOrderStatusAsync(orderId, cancellationToken)).ShouldBe(OrderStatus.ReadyToShip);
        (await CountOutboxAsync(OrderReadyToShip.EventType, cancellationToken)).ShouldBe(1);
        (await CountProcessedAsync(lastReceived.EventId, cancellationToken)).ShouldBe(1);
    }

    private OrderingDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    private ServiceProvider BuildProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GreyGray_ordering"] = _postgres.GetConnectionString(),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new StubClock(Now));
        services.AddSingleton<ICorrelationContext>(new CorrelationContext());
        services.AddSingleton(EventTypeRegistry.FromAssemblies([
            typeof(OrderReadyToShip).Assembly,
            typeof(GoodsReceived).Assembly,
        ]));
        services.AddSingleton<IPricingQuotation>(new UnusedPricing());
        services.AddOrderingModule(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task DispatchAsync<TEvent>(
        ServiceProvider provider,
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        await using var scope = provider.CreateAsyncScope();
        foreach (var handler in scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            await handler.HandleAsync(@event, cancellationToken);
        }
    }

    private static Order CreatePaidAndPurchasedOrder(
        int preorderCount,
        CampaignId campaignId,
        SkuId skuId)
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
        var lines = Enumerable.Range(0, preorderCount)
            .Select(_ => new CheckoutLine(
                skuId,
                FulfillmentMode.Preorder,
                campaignId,
                CampaignOfferId.New(),
                1,
                Money.OfMajor(100, Currency.TWD)))
            .ToArray();
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
            lines,
            $"be21-goods-received-{Guid.CreateVersion7():N}");
        var order = Order.Place(new OrderId(Guid.NewGuid()), checkout, snapshot, Now).Value;
        order.CapturePayment(order.GrandTotal).IsSuccess.ShouldBeTrue();
        foreach (var line in order.Lines)
        {
            order.RecordItemPurchased(line.Id, line.Quantity).IsSuccess.ShouldBeTrue();
        }

        return order;
    }

    private async Task<OrderStatus> ReadOrderStatusAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = NewDbContext();
        var order = await dbContext.Orders.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == orderId, cancellationToken);
        return order.Status;
    }

    private Task<int> CountOutboxAsync(string eventType, CancellationToken cancellationToken) =>
        ScalarAsync<int>("SELECT count(*)::integer FROM platform.outbox_message WHERE event_type = @event_type;",
            cancellationToken, new NpgsqlParameter("event_type", eventType));

    private Task<int> CountProcessedAsync(Guid eventId, CancellationToken cancellationToken) =>
        ScalarAsync<int>("SELECT count(*)::integer FROM platform.processed_message WHERE event_id = @event_id;",
            cancellationToken, new NpgsqlParameter("event_id", eventId));

    private async Task ResetAsync(CancellationToken cancellationToken) =>
        await ExecuteAsync("""
            TRUNCATE TABLE
                ordering.order_line,
                ordering.orders,
                platform.outbox_message,
                platform.processed_message
            CASCADE;
            """, cancellationToken);

    private async Task ExecuteAsync(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<T> ScalarAsync<T>(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Expected scalar value."));
    }

    private sealed class StubClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;

        public DateOnly TodayInTaipei => DateOnly.FromDateTime(now.UtcDateTime.AddHours(8));
    }

    /// <summary>
    /// 這條測試路徑不經由 checkout 建單（訂單直接用 <see cref="Order.Place"/> 建構後寫入），
    /// 所以 <see cref="OrderingApplicationService"/> 建構所需的 <see cref="IPricingQuotation"/>
    /// 只是滿足 DI，不會真的被呼叫。
    /// </summary>
    private sealed class UnusedPricing : IPricingQuotation
    {
        public Task<Result<PricingSnapshot>> QuoteAsync(
            QuoteRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("這個測試不應該呼叫 QuoteAsync。");

        public Task<Result<PricingSnapshotId>> FreezeAsync(
            PricingSnapshot snapshot,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("這個測試不應該呼叫 FreezeAsync。");

        public Task<Result<PricingSnapshot>> GetSnapshotAsync(
            PricingSnapshotId id,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("這個測試不應該呼叫 GetSnapshotAsync。");
    }
}
