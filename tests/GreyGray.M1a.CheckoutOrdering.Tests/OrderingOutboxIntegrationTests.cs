using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Outbox;
using GreyGray.Platform.Saga;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

public sealed class OrderingOutboxIntegrationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 29, 3, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "OrderLineCancelled 與缺貨品項由 Ordering 同一次 SaveChanges 寫入 outbox／業務表")]
    public async Task Line_cancel_is_persisted_with_its_outbox_event()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var dbContext = new OrderingDbContext(options);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        var clock = new FakeClock(Now);
        var correlation = new FakeCorrelation();
        var pricing = new FakePricing(clock);
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
        pricing.Seed(snapshot);
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
                new CheckoutLine(
                    SkuId.New(), FulfillmentMode.Preorder,
                    CampaignId.New(), CampaignOfferId.New(), 1,
                    Money.OfMajor(40, Currency.TWD)),
            ],
            "outbox-line-cancel");
        var publisher = new OutboxEventPublisher<OrderingDbContext>(
            dbContext,
            correlation,
            EventTypeRegistry.FromAssemblies([typeof(OrderPlaced).Assembly]));
        var service = new OrderingApplicationService(
            new OrderingRepository(dbContext),
            dbContext,
            publisher,
            pricing,
            clock,
            correlation);
        var created = await service.CreateFromCheckoutAsync(checkout, cancellationToken);
        await service.RecordPaymentCapturedAsync(
            created.Value.Id,
            created.Value.GrandTotal,
            cancellationToken);
        var selected = created.Value.Lines[0];

        var cancelled = await service.CancelLineAsync(
            created.Value.Id,
            selected.Id,
            "現場缺貨",
            RefundDestination.OriginalPaymentMethod,
            cancellationToken);

        cancelled.IsSuccess.ShouldBeTrue();
        var persistedLine = await dbContext.OrderLines.AsNoTracking()
            .SingleAsync(line => line.Id == selected.Id, cancellationToken);
        persistedLine.Status.ShouldBe(OrderLineStatus.Unavailable);
        persistedLine.RefundedAmountMinor.ShouldBe(selected.LineTotal.AmountMinor);

        var outbox = await dbContext.Set<OutboxMessage>().AsNoTracking()
            .SingleAsync(
                message => message.EventType == OrderLineCancelled.EventType,
                cancellationToken);
        outbox.AggregateId.ShouldBe(created.Value.Id.ToString());
        outbox.Payload.ShouldContain(selected.Id.ToString());
        outbox.Payload.ShouldContain("現場缺貨");
    }

    [Fact(DisplayName = "ADR-025：鑑賞期 timer 與訂單狀態同一次交易——中途 rollback 兩者都不落地")]
    public async Task Appraisal_timer_and_order_status_share_one_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        var clock = new FakeClock(Now);
        var correlation = new FakeCorrelation();

        OrderId orderId;
        await using (var seedContext = new OrderingDbContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync(cancellationToken);
            var pricing = new FakePricing(clock);
            var snapshot = new PricingSnapshot(
                PricingSnapshotId.New(),
                DeliveryMethod.ConvenienceStore,
                300,
                0,
                300,
                Money.OfMajor(60, Currency.TWD),
                FeeRuleSetId.New(),
                FeeRuleId.New(),
                ShippingStrategyKind.Flat,
                ["測試運費"],
                Now);
            pricing.Seed(snapshot);
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
                "outbox-appraisal-tx");
            var seedPublisher = new OutboxEventPublisher<OrderingDbContext>(
                seedContext,
                correlation,
                EventTypeRegistry.FromAssemblies([typeof(OrderPlaced).Assembly]));
            var seedService = new OrderingApplicationService(
                new OrderingRepository(seedContext),
                seedContext,
                seedPublisher,
                pricing,
                clock,
                correlation);
            var created = await seedService.CreateFromCheckoutAsync(checkout, cancellationToken);
            await seedService.RecordPaymentCapturedAsync(
                created.Value.Id,
                created.Value.GrandTotal,
                cancellationToken);
            var line = created.Value.Lines.Single();
            await seedService.RecordItemPurchasedAsync(line.Id, line.Quantity, cancellationToken);
            await seedService.RecordGoodsReceivedAsync(line.Id, cancellationToken);
            orderId = created.Value.Id;

            var seeded = await seedContext.Orders.AsNoTracking()
                .SingleAsync(order => order.Id == orderId, cancellationToken);
            seeded.Status.ShouldBe(OrderStatus.ReadyToShip, "先決條件：seed 沒有把訂單帶到 ReadyToShip。");
        }

        await using (var txContext = new OrderingDbContext(options))
        {
            var publisher = new OutboxEventPublisher<OrderingDbContext>(
                txContext,
                correlation,
                EventTypeRegistry.FromAssemblies([typeof(OrderPlaced).Assembly]));
            var fulfillmentQuery = new FakeFulfillmentQuery();
            fulfillmentQuery.SetShipments(orderId, ShipmentStatus.Delivered);
            var service = new OrderingApplicationService(
                new OrderingRepository(txContext),
                txContext,
                publisher,
                new FakePricing(clock),
                clock,
                correlation,
                fulfillmentQuery,
                new SagaTimerScheduler<OrderingDbContext>(txContext, clock),
                TimeSpan.FromDays(7));

            // 比照 IdempotentIntegrationEventHandler 的交易邊界（生產路徑一定會先開交易）。
            await using var transaction = await txContext.Database.BeginTransactionAsync(cancellationToken);
            var delivered = await service.RecordShipmentDeliveredAsync([orderId], cancellationToken);
            delivered.IsSuccess.ShouldBeTrue();

            // 刻意不 commit，模擬「timer 寫完、SaveChanges 前」中途失敗。
            await transaction.RollbackAsync(cancellationToken);
        }

        await using var verify = new OrderingDbContext(options);
        var afterRollback = await verify.Orders.AsNoTracking()
            .SingleAsync(order => order.Id == orderId, cancellationToken);
        afterRollback.Status.ShouldBe(
            OrderStatus.ReadyToShip,
            "rollback 後訂單狀態不該停在 Shipped——代表 EF 那一半確實跟著交易走。");
        afterRollback.AppraisalDueAt.ShouldBeNull("rollback 後不該留下鑑賞期到期時間。");

        var timerCount = await CountSagaTimersAsync(orderId, cancellationToken);
        timerCount.ShouldBe(
            0,
            "如果這裡是 1，代表 SagaTimerScheduler 的 raw SQL 沒有跟著 EF 的交易走——" +
            "timer 與訂單狀態就不是同一次交易，鑑賞期會排定但訂單狀態卻沒轉 Shipped。");
    }

    private async Task<long> CountSagaTimersAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM platform.saga_timer WHERE saga_id = @saga_id",
            connection);
        command.Parameters.AddWithValue("saga_id", orderId.ToString());
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }
}
