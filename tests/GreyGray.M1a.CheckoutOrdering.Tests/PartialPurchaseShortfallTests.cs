using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Pricing.Contracts;
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
/// BE-31／ADR-026：支援部分買到。買到的數量照常出貨，短缺的數量退款。
///
/// 重點在「決策延後」——標記買到 3／5 的當下**不問退款去向**，短缺的 2 件先掛在
/// <c>QuantityShortfall</c>，訂單金額一毛不動；客人選好去向、呼叫
/// <c>RefundLineShortfallAsync</c> 之後才真的退款並把 <c>Quantity</c> 減成 3。
/// 退款去向還沒決定就先改訂單總額，客人會在退款真的發生之前看到金額變小。
/// </summary>
/// <remarks>
/// 用真的 Postgres（Testcontainers）而不是 InMemory provider——帳務的不變式靠
/// CHECK constraint 守，InMemory 不會執行它們。schema 走 <c>EnsureCreatedAsync</c>，
/// 理由同 <c>OrderingGoodsReceivedHandlerTests</c>。
/// </remarks>
public sealed class PartialPurchaseShortfallTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 31, 9, 0, 0, TimeSpan.Zero);

    /// <summary>單價 100 TWD、訂 5 件，所以商品 500、運費 60、應付 560。</summary>
    private static readonly Money UnitPrice = Money.OfMajor(100, Currency.TWD);

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
        "訂 5 件買到 3 件：短缺 2 件先掛著、訂單金額不動；退款決定之後才減量並退 2 件的錢")]
    public async Task Partial_purchase_defers_the_refund_decision_then_reduces_quantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        var (orderId, lineId) = await SeedPaidOrderAsync(quantity: 5, cancellationToken);

        await using var provider = BuildProvider();

        // ── 標記買到 3／5：短缺掛著，但這一刻什麼都還沒退 ──────────────────
        await using (var scope = provider.CreateAsyncScope())
        {
            var ordering = scope.ServiceProvider.GetRequiredService<IOrderingApplication>();
            (await ordering.RecordItemPurchasedAsync(lineId, 3, cancellationToken))
                .IsSuccess.ShouldBeTrue();
        }

        var afterPurchase = await ReadLineAsync(lineId, cancellationToken);
        afterPurchase.Status.ShouldBe(OrderLineStatus.Purchased);
        afterPurchase.QuantityShortfall.ShouldBe(2);
        afterPurchase.Quantity.ShouldBe(5, "退款去向還沒決定，出貨數量不能先減。");
        afterPurchase.RefundedAmountMinor.ShouldBeNull();

        var beforeRefund = await ReadOrderAsync(orderId, cancellationToken);
        beforeRefund.GoodsTotal.ShouldBe(Money.OfMajor(500, Currency.TWD));
        beforeRefund.GrandTotal.ShouldBe(Money.OfMajor(560, Currency.TWD));
        (await CountOutboxAsync(RefundRequested.EventType, cancellationToken)).ShouldBe(0);

        // ── 客人選了原路退款：這一刻才真的退 ─────────────────────────────
        await using (var scope = provider.CreateAsyncScope())
        {
            var ordering = scope.ServiceProvider.GetRequiredService<IOrderingApplication>();
            var refunded = await ordering.RefundLineShortfallAsync(
                orderId,
                lineId,
                "現場只買到 3 件",
                RefundDestination.OriginalPaymentMethod,
                cancellationToken);
            refunded.IsSuccess.ShouldBeTrue();
        }

        var afterRefund = await ReadLineAsync(lineId, cancellationToken);
        afterRefund.Quantity.ShouldBe(3, "退款完成後 Quantity 才變成實際出貨數量。");
        afterRefund.QuantityShortfall.ShouldBe(
            2,
            "短缺過幾件是歷史事實，退款後保留原值；『退過沒有』看 RefundedAmountMinor。");
        afterRefund.RefundedAmountMinor.ShouldBe(UnitPrice.MultiplyByQuantity(2).AmountMinor);
        afterRefund.RefundedCurrency.ShouldBe(Currency.TWD);

        var afterRefundOrder = await ReadOrderAsync(orderId, cancellationToken);
        afterRefundOrder.GoodsTotal.ShouldBe(Money.OfMajor(300, Currency.TWD));
        afterRefundOrder.GrandTotal.ShouldBe(Money.OfMajor(360, Currency.TWD));

        (await CountOutboxAsync(RefundRequested.EventType, cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName =
        "冪等：同內容重送不重複；不同買到數量重送要衝突；短缺退款送兩次只退一次")]
    public async Task Recording_and_refunding_are_idempotent_and_reject_conflicting_replays()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        var (orderId, lineId) = await SeedPaidOrderAsync(quantity: 5, cancellationToken);

        await using var provider = BuildProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var ordering = scope.ServiceProvider.GetRequiredService<IOrderingApplication>();
            (await ordering.RecordItemPurchasedAsync(lineId, 3, cancellationToken))
                .IsSuccess.ShouldBeTrue();

            // 同內容重送：安靜接受，不改變任何東西。
            (await ordering.RecordItemPurchasedAsync(lineId, 3, cancellationToken))
                .IsSuccess.ShouldBeTrue();

            // 不同內容重送：必須擋下來，不能覆寫成 4 件。
            var conflict = await ordering.RecordItemPurchasedAsync(lineId, 4, cancellationToken);
            conflict.IsFailure.ShouldBeTrue();
            conflict.Error.Code.ShouldBe("ordering.purchase-already-recorded");
        }

        (await ReadLineAsync(lineId, cancellationToken)).QuantityShortfall.ShouldBe(2);

        await using (var scope = provider.CreateAsyncScope())
        {
            var ordering = scope.ServiceProvider.GetRequiredService<IOrderingApplication>();
            (await ordering.RefundLineShortfallAsync(
                orderId,
                lineId,
                "現場只買到 3 件",
                RefundDestination.OriginalPaymentMethod,
                cancellationToken)).IsSuccess.ShouldBeTrue();

            var second = await ordering.RefundLineShortfallAsync(
                orderId,
                lineId,
                "現場只買到 3 件",
                RefundDestination.OriginalPaymentMethod,
                cancellationToken);
            second.IsFailure.ShouldBeTrue();
            second.Error.Code.ShouldBe("ordering.line-shortfall-already-refunded");
        }

        // 第二次退款既沒有再減數量，也沒有再發一次事件。
        var line = await ReadLineAsync(lineId, cancellationToken);
        line.Quantity.ShouldBe(3);
        line.RefundedAmountMinor.ShouldBe(UnitPrice.MultiplyByQuantity(2).AmountMinor);
        (await ReadOrderAsync(orderId, cancellationToken)).GrandTotal
            .ShouldBe(Money.OfMajor(360, Currency.TWD));
        (await CountOutboxAsync(RefundRequested.EventType, cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "短缺退款選 StoredValue 仍被擋在 M1b，訂單一毛不動")]
    public async Task Stored_value_shortfall_refund_is_still_closed_until_m3()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        var (orderId, lineId) = await SeedPaidOrderAsync(quantity: 5, cancellationToken);

        await using var provider = BuildProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var ordering = scope.ServiceProvider.GetRequiredService<IOrderingApplication>();
            (await ordering.RecordItemPurchasedAsync(lineId, 3, cancellationToken))
                .IsSuccess.ShouldBeTrue();

            var blocked = await ordering.RefundLineShortfallAsync(
                orderId,
                lineId,
                "現場只買到 3 件",
                RefundDestination.StoredValue,
                cancellationToken);
            blocked.IsFailure.ShouldBeTrue();
            blocked.Error.Code.ShouldBe("ordering.stored-value-refund-not-available");
        }

        var line = await ReadLineAsync(lineId, cancellationToken);
        line.Quantity.ShouldBe(5);
        line.RefundedAmountMinor.ShouldBeNull();
        (await ReadOrderAsync(orderId, cancellationToken)).GrandTotal
            .ShouldBe(Money.OfMajor(560, Currency.TWD));
        (await CountOutboxAsync(RefundRequested.EventType, cancellationToken)).ShouldBe(0);
    }

    [Fact(DisplayName = "訂 5 件只買到 1 件：退款後 Quantity 是 1，沒有踩到 ck_order_line_quantity")]
    public async Task Refunding_almost_everything_still_leaves_a_valid_quantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        var (orderId, lineId) = await SeedPaidOrderAsync(quantity: 5, cancellationToken);

        await using var provider = BuildProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var ordering = scope.ServiceProvider.GetRequiredService<IOrderingApplication>();
            (await ordering.RecordItemPurchasedAsync(lineId, 1, cancellationToken))
                .IsSuccess.ShouldBeTrue();
            (await ordering.RefundLineShortfallAsync(
                orderId,
                lineId,
                "現場只買到 1 件",
                RefundDestination.OriginalPaymentMethod,
                cancellationToken)).IsSuccess.ShouldBeTrue();
        }

        // quantity BETWEEN 1 AND 999：全部沒買到走的是既有的整條取消路徑，不是這一條，
        // 所以 Quantity 最少只會減到 1。SaveChanges 沒噴 23514 就代表這個推導成立。
        var line = await ReadLineAsync(lineId, cancellationToken);
        line.Quantity.ShouldBe(1);
        line.QuantityShortfall.ShouldBe(4);
        line.RefundedAmountMinor.ShouldBe(UnitPrice.MultiplyByQuantity(4).AmountMinor);
        (await ReadOrderAsync(orderId, cancellationToken)).GrandTotal
            .ShouldBe(Money.OfMajor(160, Currency.TWD));
    }

    [Fact(DisplayName = "全數買到的品項沒有短缺可退")]
    public async Task A_fully_purchased_line_has_no_shortfall_to_refund()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetAsync(cancellationToken);
        var (orderId, lineId) = await SeedPaidOrderAsync(quantity: 5, cancellationToken);

        await using var provider = BuildProvider();

        await using var scope = provider.CreateAsyncScope();
        var ordering = scope.ServiceProvider.GetRequiredService<IOrderingApplication>();
        (await ordering.RecordItemPurchasedAsync(lineId, 5, cancellationToken))
            .IsSuccess.ShouldBeTrue();

        (await ReadLineAsync(lineId, cancellationToken)).QuantityShortfall.ShouldBe(0);

        var refused = await ordering.RefundLineShortfallAsync(
            orderId,
            lineId,
            "沒有短缺",
            RefundDestination.OriginalPaymentMethod,
            cancellationToken);
        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("ordering.order-line-has-no-shortfall");
    }

    private async Task<(OrderId OrderId, OrderLineId LineId)> SeedPaidOrderAsync(
        int quantity,
        CancellationToken cancellationToken)
    {
        await using var seedContext = NewDbContext();
        var order = CreatePaidPreorder(quantity);
        seedContext.Orders.Add(order);
        await seedContext.SaveChangesAsync(cancellationToken);
        return (order.Id, order.Lines[0].Id);
    }

    private static Order CreatePaidPreorder(int quantity)
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
                    SkuId.New(),
                    FulfillmentMode.Preorder,
                    CampaignId.New(),
                    CampaignOfferId.New(),
                    quantity,
                    UnitPrice),
            ],
            $"be31-shortfall-{Guid.CreateVersion7():N}");
        var order = Order.Place(new OrderId(Guid.NewGuid()), checkout, snapshot, Now).Value;
        order.CapturePayment(order.GrandTotal).IsSuccess.ShouldBeTrue();
        return order;
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
            typeof(RefundRequested).Assembly,
        ]));
        services.AddSingleton<IPricingQuotation>(new UnusedPricing());
        services.AddOrderingModule(configuration);
        return services.BuildServiceProvider();
    }

    private async Task<OrderLine> ReadLineAsync(
        OrderLineId lineId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = NewDbContext();
        return await dbContext.OrderLines.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == lineId, cancellationToken);
    }

    private async Task<Order> ReadOrderAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        await using var dbContext = NewDbContext();
        return await dbContext.Orders.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == orderId, cancellationToken);
    }

    private async Task<int> CountOutboxAsync(string eventType, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*)::integer FROM platform.outbox_message WHERE event_type = @event_type;",
            connection);
        command.Parameters.Add(new NpgsqlParameter("event_type", eventType));
        return (int)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Expected scalar value."));
    }

    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            TRUNCATE TABLE
                ordering.order_line,
                ordering.orders,
                platform.outbox_message,
                platform.processed_message
            CASCADE;
            """,
            connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class StubClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;

        public DateOnly TodayInTaipei => DateOnly.FromDateTime(now.UtcDateTime.AddHours(8));
    }

    /// <summary>訂單直接用 <see cref="Order.Place"/> 建構後寫入，不經 checkout，所以不會被呼叫。</summary>
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
