using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Shared.Kernel;
using Shouldly;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

public sealed class OrderingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 8, 30, 0, TimeSpan.Zero);

    [Fact(DisplayName = "CheckoutCompleted 以 CartId 冪等建單，首次同 UoW 發 OrderPlaced + PaymentRequested")]
    public async Task Checkout_creates_order_idempotently()
    {
        var fixture = new OrderingFixture();

        var first = await fixture.Service.CreateFromCheckoutAsync(
            fixture.Checkout,
            TestContext.Current.CancellationToken);
        var replay = await fixture.Service.CreateFromCheckoutAsync(
            fixture.Checkout,
            TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        replay.IsSuccess.ShouldBeTrue();
        replay.Value.Id.ShouldBe(first.Value.Id);
        first.Value.Status.ShouldBe(OrderStatus.AwaitingPayment);
        first.Value.OrderNumber.ShouldStartWith("GG260828");
        fixture.UnitOfWork.Saves.ShouldBe(1);
        fixture.Publisher.Published.Count.ShouldBe(2);
        fixture.Publisher.Published[0].ShouldBeOfType<OrderPlaced>();
        fixture.Publisher.Published[1].ShouldBeOfType<PaymentRequested>();
    }

    [Fact(DisplayName = "客戶只能取消 AwaitingPayment，零退款不發 RefundRequested")]
    public async Task Customer_cancel_is_awaiting_payment_only()
    {
        var fixture = new OrderingFixture();
        var created = await fixture.Service.CreateFromCheckoutAsync(
            fixture.Checkout,
            TestContext.Current.CancellationToken);
        fixture.Publisher.Reset();
        fixture.UnitOfWork.Reset();

        var cancelled = await fixture.Service.CancelCustomerAsync(
            fixture.CustomerId,
            created.Value.Id,
            "改變心意",
            TestContext.Current.CancellationToken);

        cancelled.IsSuccess.ShouldBeTrue();
        cancelled.Value.Status.ShouldBe(OrderStatus.Cancelled);
        fixture.Publisher.Published.ShouldHaveSingleItem()
            .ShouldBeOfType<OrderCancelled>();
        fixture.Publisher.Published.OfType<RefundRequested>().ShouldBeEmpty();

        var again = await fixture.Service.CancelCustomerAsync(
            fixture.CustomerId,
            created.Value.Id,
            null,
            TestContext.Current.CancellationToken);
        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe("ordering.cannot-self-cancel-after-payment");
    }

    [Fact(DisplayName = "付款後發 OrderPaid；Admin 整單取消發 OrderCancelled + RefundRequested")]
    public async Task Paid_admin_cancel_requests_whole_order_refund()
    {
        var fixture = new OrderingFixture();
        var created = await fixture.Service.CreateFromCheckoutAsync(
            fixture.Checkout,
            TestContext.Current.CancellationToken);
        fixture.Publisher.Reset();
        fixture.UnitOfWork.Reset();

        var paid = await fixture.Service.RecordPaymentCapturedAsync(
            created.Value.Id,
            created.Value.GrandTotal,
            TestContext.Current.CancellationToken);

        paid.IsSuccess.ShouldBeTrue();
        fixture.Publisher.Published.ShouldHaveSingleItem()
            .ShouldBeOfType<OrderPaid>();
        var detail = await fixture.Service.GetAdminAsync(
            created.Value.Id,
            TestContext.Current.CancellationToken);
        detail.Value.Status.ShouldBe(OrderStatus.PaidAwaitingClose);
        fixture.Publisher.Reset();

        var cancelled = await fixture.Service.CancelAdminAsync(
            created.Value.Id,
            "團務異常",
            RefundDestination.StoredValue,
            TestContext.Current.CancellationToken);

        cancelled.IsSuccess.ShouldBeTrue();
        cancelled.Value.Status.ShouldBe(OrderStatus.Cancelled);
        fixture.Publisher.Published.Count.ShouldBe(2);
        var cancelledEvent = fixture.Publisher.Published.OfType<OrderCancelled>().Single();
        cancelledEvent.RefundAmount.ShouldBe(created.Value.GrandTotal);
        cancelledEvent.RefundTo.ShouldBe(RefundDestination.StoredValue);
        var refund = fixture.Publisher.Published.OfType<RefundRequested>().Single();
        refund.LineId.ShouldBeNull("整單取消的退款事件不可誤掛到單一品項。");
        refund.Amount.ShouldBe(created.Value.GrandTotal);
    }

    [Fact(DisplayName = "Admin 缺貨取消整條 line，退款 lineTotal 且其餘 line／訂單狀態不變")]
    public async Task Admin_line_cancel_refunds_only_selected_line()
    {
        var fixture = new OrderingFixture(includeSecondLine: true);
        var created = await fixture.Service.CreateFromCheckoutAsync(
            fixture.Checkout,
            TestContext.Current.CancellationToken);
        await fixture.Service.RecordPaymentCapturedAsync(
            created.Value.Id,
            created.Value.GrandTotal,
            TestContext.Current.CancellationToken);
        fixture.Publisher.Reset();
        fixture.UnitOfWork.Reset();
        var selected = created.Value.Lines[0];
        var untouched = created.Value.Lines[1];

        var cancelled = await fixture.Service.CancelLineAsync(
            created.Value.Id,
            selected.Id,
            "現場缺貨",
            RefundDestination.StoredValue,
            TestContext.Current.CancellationToken);

        cancelled.IsSuccess.ShouldBeTrue();
        cancelled.Value.Status.ShouldBe(OrderStatus.PaidAwaitingClose);
        var cancelledLine = cancelled.Value.Lines.Single(line => line.Id == selected.Id);
        cancelledLine.Status.ShouldBe(OrderLineStatus.Unavailable);
        cancelledLine.RefundedAmount.ShouldBe(selected.LineTotal);
        cancelled.Value.Lines.Single(line => line.Id == untouched.Id).Status
            .ShouldBe(untouched.Status);
        cancelled.Value.GoodsTotal.ShouldBe(created.Value.GoodsTotal - selected.LineTotal);
        cancelled.Value.GrandTotal.ShouldBe(created.Value.GrandTotal - selected.LineTotal);
        cancelled.Value.ShippingFee.ShouldBe(created.Value.ShippingFee);
        fixture.UnitOfWork.Saves.ShouldBe(1);

        var lineEvent = fixture.Publisher.Published.OfType<OrderLineCancelled>().Single();
        lineEvent.LineId.ShouldBe(selected.Id);
        lineEvent.RefundAmount.ShouldBe(selected.LineTotal);
        var refund = fixture.Publisher.Published.OfType<RefundRequested>().Single();
        refund.LineId.ShouldBe(selected.Id);
        refund.Amount.ShouldBe(selected.LineTotal);
    }

    [Fact(DisplayName = "M1b ItemPurchased 把對應 line 與訂單推進採購中，事件重送冪等")]
    public async Task Item_purchased_advances_order_and_line_idempotently()
    {
        var fixture = new OrderingFixture();
        var created = await fixture.Service.CreateFromCheckoutAsync(
            fixture.Checkout,
            TestContext.Current.CancellationToken);
        await fixture.Service.RecordPaymentCapturedAsync(
            created.Value.Id,
            created.Value.GrandTotal,
            TestContext.Current.CancellationToken);
        fixture.UnitOfWork.Reset();
        var line = created.Value.Lines.Single();

        var first = await fixture.Service.RecordItemPurchasedAsync(
            line.Id,
            line.Quantity,
            TestContext.Current.CancellationToken);
        var replay = await fixture.Service.RecordItemPurchasedAsync(
            line.Id,
            line.Quantity,
            TestContext.Current.CancellationToken);
        var detail = await fixture.Service.GetAdminAsync(
            created.Value.Id,
            TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        replay.IsSuccess.ShouldBeTrue();
        detail.Value.Status.ShouldBe(OrderStatus.Purchasing);
        detail.Value.Lines.Single().Status.ShouldBe(OrderLineStatus.Purchased);
        fixture.UnitOfWork.Saves.ShouldBe(1);
    }

    private sealed class OrderingFixture
    {
        public OrderingFixture(bool includeSecondLine = false)
        {
            CustomerId = CustomerId.New();
            Clock = new FakeClock(Now);
            Pricing = new FakePricing(Clock);
            var snapshot = new PricingSnapshot(
                PricingSnapshotId.New(),
                DeliveryMethod.ConvenienceStore,
                300,
                0,
                300,
                new Money(6_000, Currency.TWD),
                FeeRuleSetId.New(),
                FeeRuleId.New(),
                ShippingStrategyKind.Flat,
                ["超商一口價 NT$60"],
                Now);
            Pricing.Seed(snapshot);
            var lines = new List<CheckoutLine>
            {
                new(
                    SkuId.New(),
                    FulfillmentMode.Preorder,
                    CampaignId.New(),
                    CampaignOfferId.New(),
                    2,
                    new Money(10_000, Currency.TWD)),
            };
            if (includeSecondLine)
            {
                lines.Add(new CheckoutLine(
                    SkuId.New(),
                    FulfillmentMode.Preorder,
                    CampaignId.New(),
                    CampaignOfferId.New(),
                    1,
                    new Money(5_000, Currency.TWD)));
            }

            Checkout = new CheckoutCompleted(
                Guid.CreateVersion7(),
                Now,
                TenantId.Default,
                CartId.New(),
                CustomerId,
                null,
                DeliveryMethod.ConvenienceStore,
                ShippingPolicy.HoldUntilComplete,
                snapshot.Id,
                lines,
                "checkout-idempotency-1")
            {
                ConvenienceStoreCode = "991234",
            };
            UnitOfWork = new FakeUnitOfWork();
            Publisher = new FakeEventPublisher();
            Service = new OrderingApplicationService(
                new FakeOrderRepository(),
                UnitOfWork,
                Publisher,
                Pricing,
                Clock,
                new FakeCorrelation());
        }

        public CustomerId CustomerId { get; }

        public FakeClock Clock { get; }

        public FakePricing Pricing { get; }

        public CheckoutCompleted Checkout { get; }

        public FakeUnitOfWork UnitOfWork { get; }

        public FakeEventPublisher Publisher { get; }

        public OrderingApplicationService Service { get; }
    }
}
