using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Shared.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

public sealed class CampaignOrderQueryAdapterTests
{
    [Fact(DisplayName = "Ordering module 註冊 Campaign 所需的訂單投影 adapter")]
    public void Module_registers_campaign_order_query()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddOrderingModule(configuration);

        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(ICampaignOrderQuery)
            && descriptor.ImplementationType == typeof(CampaignOrderQueryAdapter));
    }

    [Fact(DisplayName = "Campaign 投影聚合 SKU 數量並將取消訂單視為已終止")]
    public async Task Projection_aggregates_quantities_and_terminal_statuses()
    {
        var campaignId = new CampaignId(Guid.CreateVersion7());
        var firstSku = new SkuId(Guid.CreateVersion7());
        var secondSku = new SkuId(Guid.CreateVersion7());
        var query = new StubOrderQuery(
        [
            Order(campaignId, OrderStatus.Shipped, (firstSku, 2), (secondSku, 1)),
            Order(campaignId, OrderStatus.Cancelled, (firstSku, 3)),
        ]);

        var result = await new CampaignOrderQueryAdapter(query)
            .GetAsync(campaignId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OrderCount.ShouldBe(2);
        result.Value.OrderedQuantityBySku[firstSku].ShouldBe(5);
        result.Value.OrderedQuantityBySku[secondSku].ShouldBe(1);
        result.Value.AllOrdersShipped.ShouldBeTrue();
        query.RequestedCampaign.ShouldBe(campaignId);
    }

    [Fact(DisplayName = "尚未出貨訂單會阻擋 Campaign 結團")]
    public async Task Projection_rejects_non_terminal_order_as_all_shipped()
    {
        var campaignId = new CampaignId(Guid.CreateVersion7());
        var query = new StubOrderQuery(
        [
            Order(
                campaignId,
                OrderStatus.ReadyToShip,
                (new SkuId(Guid.CreateVersion7()), 1)),
        ]);

        var result = await new CampaignOrderQueryAdapter(query)
            .GetAsync(campaignId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AllOrdersShipped.ShouldBeFalse();
    }

    private static OrderView Order(
        CampaignId campaignId,
        OrderStatus status,
        params (SkuId SkuId, int Quantity)[] lines)
    {
        var zero = Money.Zero(Currency.TWD);
        return new OrderView(
            OrderId.New(),
            new CustomerId(Guid.CreateVersion7()),
            SourceChannel.Own,
            status,
            ShippingPolicy.HoldUntilComplete,
            new PricingSnapshotId(Guid.CreateVersion7()),
            zero,
            zero,
            zero,
            lines.Select(line => new OrderLineView(
                OrderLineId.New(),
                line.SkuId,
                FulfillmentMode.Preorder,
                OrderLineStatus.Pending,
                line.Quantity,
                zero,
                campaignId,
                null)).ToArray(),
            DateTimeOffset.Parse("2026-08-28T00:00:00Z"));
    }

    private sealed class StubOrderQuery(IReadOnlyList<OrderView> orders) : IOrderQuery
    {
        public CampaignId? RequestedCampaign { get; private set; }

        public Task<Result<OrderView>> GetAsync(
            OrderId id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<OrderView>.Failure("test.not-used", "not used"));

        public Task<Result<IReadOnlyList<OrderView>>> GetByCampaignAsync(
            CampaignId campaignId,
            CancellationToken cancellationToken)
        {
            RequestedCampaign = campaignId;
            return Task.FromResult(Result<IReadOnlyList<OrderView>>.Success(orders));
        }
    }
}
