using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
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
            RefundDestination.StoredValue,
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
}
