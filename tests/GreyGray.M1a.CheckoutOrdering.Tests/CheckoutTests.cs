using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Checkout.Core;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;
using Shouldly;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

public sealed class CheckoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "純 quote 只呼叫 Pricing.Quote，不 Save、不 Freeze、不發事件")]
    public async Task Quote_is_pure()
    {
        var fixture = new CheckoutFixture();
        var added = await fixture.Service.AddLineAsync(
            new AddCartLineRequest(
                fixture.CartId,
                null,
                fixture.Sku.Id,
                FulfillmentMode.Preorder,
                fixture.Offer.Id,
                2),
            TestContext.Current.CancellationToken);
        added.IsSuccess.ShouldBeTrue();
        fixture.UnitOfWork.Reset();
        fixture.Publisher.Reset();

        var quoted = await fixture.Service.QuoteAsync(
            new QuoteCartRequest(
                fixture.CartId,
                null,
                GreyGray.Modules.Pricing.Contracts.DeliveryMethod.ConvenienceStore),
            TestContext.Current.CancellationToken);

        quoted.IsSuccess.ShouldBeTrue();
        quoted.Value.GoodsTotal.ShouldBe(new Money(20_000, Currency.TWD));
        quoted.Value.GrandTotal.ShouldBe(new Money(26_000, Currency.TWD));
        fixture.UnitOfWork.Saves.ShouldBe(0);
        fixture.Pricing.FreezeCalls.ShouldBe(0);
        fixture.Publisher.Published.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Stock 只使用 Catalog 的公開標價且仍檢查庫存")]
    public async Task Stock_uses_trusted_catalog_list_price()
    {
        var fixture = new CheckoutFixture();
        var result = await fixture.Service.AddLineAsync(
            new AddCartLineRequest(
                fixture.CartId,
                null,
                fixture.Sku.Id,
                FulfillmentMode.Stock,
                null,
                1),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Lines.ShouldHaveSingleItem().UnitPrice
            .ShouldBe(new Money(12_000, Currency.TWD));
        fixture.UnitOfWork.Saves.ShouldBe(1);
    }

    [Fact(DisplayName = "結帳凍結報價並以同一工作單元寫 Cart + CheckoutCompleted，重送不重發")]
    public async Task Complete_publishes_once_and_replays_idempotently()
    {
        var fixture = new CheckoutFixture();
        var added = await fixture.Service.AddLineAsync(
            new AddCartLineRequest(
                fixture.CartId,
                fixture.Customer.Id,
                fixture.Sku.Id,
                FulfillmentMode.Preorder,
                fixture.Offer.Id,
                1),
            TestContext.Current.CancellationToken);
        added.IsSuccess.ShouldBeTrue();
        fixture.UnitOfWork.Reset();
        fixture.Publisher.Reset();

        var request = new CompleteCheckoutRequest(
            fixture.CartId,
            fixture.Customer.Id,
            GreyGray.Modules.Pricing.Contracts.DeliveryMethod.SelfPickup,
            ShippingPolicy.HoldUntilComplete,
            null,
            null,
            "請小心包裝",
            "checkout-key-1")
        {
            ConvenienceStoreName = "模擬門市（dev）",
            ConvenienceStoreAddress = "台北市模擬路 1 號",
        };
        var first = await fixture.Service.CompleteAsync(
            request,
            TestContext.Current.CancellationToken);
        var replay = await fixture.Service.CompleteAsync(
            request,
            TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        replay.IsSuccess.ShouldBeTrue();
        replay.Value.EventId.ShouldBe(first.Value.EventId);
        first.Value.BuyerNote.ShouldBe("請小心包裝");
        replay.Value.ConvenienceStoreName.ShouldBe("模擬門市（dev）");
        replay.Value.ConvenienceStoreAddress.ShouldBe("台北市模擬路 1 號");
        fixture.Pricing.FreezeCalls.ShouldBe(1);
        fixture.UnitOfWork.Saves.ShouldBe(1);
        fixture.Publisher.Published.ShouldHaveSingleItem()
            .ShouldBeOfType<CheckoutCompleted>();
    }

    private sealed class CheckoutFixture
    {
        public CheckoutFixture()
        {
            Sku = new SkuSnapshot(
                SkuId.New(),
                ProductId.New(),
                "面膜",
                "10 入",
                300,
                new Dimensions(20, 15, 5),
                true)
            {
                ListPrice = new Money(12_000, Currency.TWD),
            };
            Offer = new CampaignOffer(
                CampaignOfferId.New(),
                CampaignId.New(),
                Sku.Id,
                new Money(10_000, Currency.TWD),
                null,
                true);
            Customer = new CustomerSummary(CustomerId.New(), "灰灰", MemberTier.Standard, true);
            Clock = new FakeClock(Now);
            Pricing = new FakePricing(Clock);
            UnitOfWork = new FakeUnitOfWork();
            Publisher = new FakeEventPublisher();
            Service = new CheckoutApplicationService(
                new FakeCartRepository(),
                UnitOfWork,
                Publisher,
                new FakeCatalogQuery(Sku),
                new FakeCampaignQuery(Offer),
                new FakeInventoryQuery(Sku.Id, 10),
                Pricing,
                new FakePaymentQuery(),
                new FakeCustomerDirectory(Customer),
                Clock,
                new FakeCorrelation());
        }

        public CartId CartId { get; } = CartId.New();

        public SkuSnapshot Sku { get; }

        public CampaignOffer Offer { get; }

        public CustomerSummary Customer { get; }

        public FakeClock Clock { get; }

        public FakePricing Pricing { get; }

        public FakeUnitOfWork UnitOfWork { get; }

        public FakeEventPublisher Publisher { get; }

        public CheckoutApplicationService Service { get; }
    }
}
