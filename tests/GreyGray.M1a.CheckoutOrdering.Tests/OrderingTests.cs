using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Saga;
using GreyGray.Platform.Messaging;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        first.Value.ConvenienceStoreCode.ShouldBe("991234");
        first.Value.ConvenienceStoreName.ShouldBe("模擬門市（dev）");
        first.Value.ConvenienceStoreAddress.ShouldBe("台北市模擬路 1 號");
        fixture.UnitOfWork.Saves.ShouldBe(1);
        fixture.Publisher.Published.Count.ShouldBe(2);
        fixture.Publisher.Published[0].ShouldBeOfType<OrderPlaced>();
        fixture.Publisher.Published[1].ShouldBeOfType<PaymentRequested>();
    }

    [Fact(DisplayName = "舊 CheckoutCompleted.v1 JSON 經事件登錄與真的 handler 建單，新門市欄位為 null")]
    public async Task Old_checkout_event_json_replays_through_the_real_worker_handler()
    {
        var fixture = new OrderingFixture();
        var oldEvent = fixture.Checkout with
        {
            ConvenienceStoreName = null,
            ConvenienceStoreAddress = null,
        };
        var registry = EventTypeRegistry.FromAssemblies([typeof(CheckoutCompleted).Assembly]);
        var currentJson = JsonSerializer.Serialize(
            oldEvent,
            registry.JsonTypeInfoOf(typeof(CheckoutCompleted)));
        var oldPayload = JsonNode.Parse(currentJson)!.AsObject();
        oldPayload.Remove("convenienceStoreName");
        oldPayload.Remove("convenienceStoreAddress");
        var json = oldPayload.ToJsonString();
        json.ShouldNotContain("convenienceStoreName");
        json.ShouldNotContain("convenienceStoreAddress");
        var deserialized = (CheckoutCompleted?)JsonSerializer.Deserialize(
            json,
            registry.JsonTypeInfoOf(typeof(CheckoutCompleted)));
        deserialized.ShouldNotBeNull();

        var handler = new CheckoutCompletedHandler(fixture.Service);
        await handler.HandleAsync(deserialized!, TestContext.Current.CancellationToken);

        var order = (await fixture.Service.GetAdminAsync(
            (await fixture.Service.ListAdminAsync(
                new AdminOrderListRequest(null, null, null, null),
                TestContext.Current.CancellationToken)).Value.Items.ShouldHaveSingleItem().Id,
            TestContext.Current.CancellationToken)).Value;
        order.ConvenienceStoreCode.ShouldBe("991234");
        order.ConvenienceStoreName.ShouldBeNull();
        order.ConvenienceStoreAddress.ShouldBeNull();
    }

    [Fact(DisplayName = "現在卡在哪 #21：同一視窗內連續建兩張訂單，OrderNumber 不能撞號")]
    public async Task Order_numbers_do_not_collide_for_orders_created_moments_apart()
    {
        var fixture = new OrderingFixture();
        var secondCheckout = fixture.Checkout with
        {
            EventId = Guid.CreateVersion7(),
            CartId = CartId.New(),
            IdempotencyKey = "checkout-idempotency-2",
        };

        var first = await fixture.Service.CreateFromCheckoutAsync(
            fixture.Checkout,
            TestContext.Current.CancellationToken);
        var second = await fixture.Service.CreateFromCheckoutAsync(
            secondCheckout,
            TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        second.Value.Id.ShouldNotBe(first.Value.Id);
        second.Value.OrderNumber.ShouldNotBe(first.Value.OrderNumber);
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
            RefundDestination.OriginalPaymentMethod,
            TestContext.Current.CancellationToken);

        cancelled.IsSuccess.ShouldBeTrue();
        cancelled.Value.Status.ShouldBe(OrderStatus.Cancelled);
        fixture.Publisher.Published.Count.ShouldBe(2);
        var cancelledEvent = fixture.Publisher.Published.OfType<OrderCancelled>().Single();
        cancelledEvent.RefundAmount.ShouldBe(created.Value.GrandTotal);
        cancelledEvent.RefundTo.ShouldBe(RefundDestination.OriginalPaymentMethod);
        var refund = fixture.Publisher.Published.OfType<RefundRequested>().Single();
        refund.LineId.ShouldBeNull("整單取消的退款事件不可誤掛到單一品項。");
        refund.Amount.ShouldBe(created.Value.GrandTotal);
    }

    [Fact(DisplayName = "ADR-023／024：儲值金退款 M1b 尚未開放，Admin 整單取消回業務失敗且不改動訂單")]
    public async Task Admin_cancel_rejects_stored_value_refund_until_m3()
    {
        var fixture = new OrderingFixture();
        var created = await fixture.Service.CreateFromCheckoutAsync(
            fixture.Checkout,
            TestContext.Current.CancellationToken);
        await fixture.Service.RecordPaymentCapturedAsync(
            created.Value.Id,
            created.Value.GrandTotal,
            TestContext.Current.CancellationToken);
        fixture.Publisher.Reset();
        fixture.UnitOfWork.Reset();

        var rejected = await fixture.Service.CancelAdminAsync(
            created.Value.Id,
            "團務異常",
            RefundDestination.StoredValue,
            TestContext.Current.CancellationToken);

        rejected.IsFailure.ShouldBeTrue();
        rejected.Error.Code.ShouldBe("ordering.stored-value-refund-not-available");
        fixture.Publisher.Published.ShouldBeEmpty();
        fixture.UnitOfWork.Saves.ShouldBe(0);
        var detail = await fixture.Service.GetAdminAsync(
            created.Value.Id,
            TestContext.Current.CancellationToken);
        detail.Value.Status.ShouldBe(OrderStatus.PaidAwaitingClose);
    }

    [Fact(DisplayName = "ADR-023／024：儲值金退款 M1b 尚未開放，Admin 缺貨整條 line 取消同樣回業務失敗")]
    public async Task Line_cancel_rejects_stored_value_refund_until_m3()
    {
        var fixture = new OrderingFixture();
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

        var rejected = await fixture.Service.CancelLineAsync(
            created.Value.Id,
            selected.Id,
            "現場缺貨",
            RefundDestination.StoredValue,
            TestContext.Current.CancellationToken);

        rejected.IsFailure.ShouldBeTrue();
        rejected.Error.Code.ShouldBe("ordering.stored-value-refund-not-available");
        fixture.Publisher.Published.ShouldBeEmpty();
        fixture.UnitOfWork.Saves.ShouldBe(0);
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
            RefundDestination.OriginalPaymentMethod,
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

    [Fact(DisplayName = "ADR-025：出貨單全部簽收才起算鑑賞期，屆滿轉 Completed")]
    public async Task Appraisal_period_starts_after_full_delivery_and_completes_on_timeout()
    {
        var fixture = new OrderingFixture();
        var order = await CreateReadyToShipOrderAsync(fixture);
        fixture.FulfillmentQuery.SetShipments(order.Id, ShipmentStatus.Delivered);
        fixture.Publisher.Reset();
        fixture.UnitOfWork.Reset();

        var delivered = await fixture.Service.RecordShipmentDeliveredAsync(
            [order.Id],
            TestContext.Current.CancellationToken);

        delivered.IsSuccess.ShouldBeTrue();
        var afterDelivery = await fixture.Service.GetAdminAsync(
            order.Id,
            TestContext.Current.CancellationToken);
        afterDelivery.Value.Status.ShouldBe(OrderStatus.Shipped);
        var timer = fixture.TimerScheduler.Scheduled.ShouldHaveSingleItem();
        timer.SagaType.ShouldBe("ordering.appraisal-period");
        timer.SagaId.ShouldBe(order.Id.ToString());
        timer.FireAt.ShouldBe(fixture.Clock.UtcNow + fixture.AppraisalPeriod);
        fixture.UnitOfWork.Saves.ShouldBe(1);

        fixture.Clock.UtcNow = timer.FireAt;
        await fixture.Service.ResolveAppraisalTimeoutAsync(
            order.Id,
            TestContext.Current.CancellationToken);

        var completed = await fixture.Service.GetAdminAsync(
            order.Id,
            TestContext.Current.CancellationToken);
        completed.Value.Status.ShouldBe(OrderStatus.Completed);
        fixture.Publisher.Published.OfType<OrderCompleted>().ShouldHaveSingleItem();
    }

    [Fact(DisplayName = "ADR-025：一張訂單對應多個出貨單，只簽收一張時不起算鑑賞期")]
    public async Task Appraisal_period_waits_for_all_shipments_to_be_delivered()
    {
        var fixture = new OrderingFixture();
        var order = await CreateReadyToShipOrderAsync(fixture);
        fixture.FulfillmentQuery.SetShipments(
            order.Id,
            ShipmentStatus.Delivered,
            ShipmentStatus.Dispatched);
        fixture.Publisher.Reset();
        fixture.UnitOfWork.Reset();

        var firstDelivery = await fixture.Service.RecordShipmentDeliveredAsync(
            [order.Id],
            TestContext.Current.CancellationToken);

        firstDelivery.IsSuccess.ShouldBeTrue();
        fixture.TimerScheduler.Scheduled.ShouldBeEmpty();
        fixture.UnitOfWork.Saves.ShouldBe(0);
        var stillReadyToShip = await fixture.Service.GetAdminAsync(
            order.Id,
            TestContext.Current.CancellationToken);
        stillReadyToShip.Value.Status.ShouldBe(OrderStatus.ReadyToShip);

        fixture.FulfillmentQuery.SetShipments(
            order.Id,
            ShipmentStatus.Delivered,
            ShipmentStatus.Delivered);
        var secondDelivery = await fixture.Service.RecordShipmentDeliveredAsync(
            [order.Id],
            TestContext.Current.CancellationToken);

        secondDelivery.IsSuccess.ShouldBeTrue();
        fixture.TimerScheduler.Scheduled.ShouldHaveSingleItem();
        var nowShipped = await fixture.Service.GetAdminAsync(
            order.Id,
            TestContext.Current.CancellationToken);
        nowShipped.Value.Status.ShouldBe(OrderStatus.Shipped);
    }

    [Fact(DisplayName = "ADR-025：鑑賞期內被取消，timer 屆滿不會把訂單拉回 Completed")]
    public async Task Cancelled_order_is_not_pulled_back_to_completed_by_appraisal_timeout()
    {
        var fixture = new OrderingFixture();
        var order = await CreateReadyToShipOrderAsync(fixture);
        fixture.FulfillmentQuery.SetShipments(order.Id, ShipmentStatus.Delivered);
        await fixture.Service.RecordShipmentDeliveredAsync(
            [order.Id],
            TestContext.Current.CancellationToken);
        var timer = fixture.TimerScheduler.Scheduled.Single();

        var cancelled = await fixture.Service.CancelAdminAsync(
            order.Id,
            "客訴退貨",
            RefundDestination.OriginalPaymentMethod,
            TestContext.Current.CancellationToken);
        cancelled.IsSuccess.ShouldBeTrue();
        cancelled.Value.Status.ShouldBe(OrderStatus.Cancelled);
        fixture.Publisher.Reset();

        fixture.Clock.UtcNow = timer.FireAt;
        await fixture.Service.ResolveAppraisalTimeoutAsync(
            order.Id,
            TestContext.Current.CancellationToken);

        var afterTimeout = await fixture.Service.GetAdminAsync(
            order.Id,
            TestContext.Current.CancellationToken);
        afterTimeout.Value.Status.ShouldBe(OrderStatus.Cancelled);
        fixture.Publisher.Published.OfType<OrderCompleted>().ShouldBeEmpty();
    }

    [Fact(DisplayName = "鑑賞期 timer 對應的訂單已不存在時安靜 no-op，不丟例外")]
    public async Task Appraisal_timeout_for_missing_order_is_a_silent_noop()
    {
        var fixture = new OrderingFixture();

        await Should.NotThrowAsync(() =>
            fixture.Service.ResolveAppraisalTimeoutAsync(
                OrderId.New(),
                TestContext.Current.CancellationToken));

        fixture.Publisher.Published.ShouldBeEmpty();
    }

    [Fact(DisplayName =
        "#42：交運把品項推到 Shipped；缺貨品項不動、訂單狀態不動，第二張出貨單重放冪等")]
    public async Task Shipment_dispatch_moves_lines_to_shipped_without_touching_order_status()
    {
        // #42：OrderLineStatus 從下單到訂單完成都停在 Pending——Reserved/Shipped/Completed
        // 三個值從來沒有被指派過，所以前台品項永遠顯示「處理中」。
        var fixture = new OrderingFixture(includeSecondLine: true);
        var created = await fixture.Service.CreateFromCheckoutAsync(
            fixture.Checkout,
            TestContext.Current.CancellationToken);
        await fixture.Service.RecordPaymentCapturedAsync(
            created.Value.Id,
            created.Value.GrandTotal,
            TestContext.Current.CancellationToken);
        var shippedLineId = created.Value.Lines[0].Id;
        var unavailableLineId = created.Value.Lines[1].Id;
        var cancelledLine = await fixture.Service.CancelLineAsync(
            created.Value.Id,
            unavailableLineId,
            "現場缺貨",
            RefundDestination.OriginalPaymentMethod,
            TestContext.Current.CancellationToken);
        cancelledLine.IsSuccess.ShouldBeTrue();

        var before = await fixture.Service.GetAdminAsync(
            created.Value.Id,
            TestContext.Current.CancellationToken);
        var orderStatusBeforeDispatch = before.Value.Status;
        fixture.UnitOfWork.Reset();

        var dispatched = await fixture.Service.RecordShipmentDispatchedAsync(
            [created.Value.Id],
            TestContext.Current.CancellationToken);
        // 一張訂單可以拆進多張出貨單（N:M），第二張交運時會再帶到同一個 orderId。
        var secondShipment = await fixture.Service.RecordShipmentDispatchedAsync(
            [created.Value.Id],
            TestContext.Current.CancellationToken);

        dispatched.IsSuccess.ShouldBeTrue();
        secondShipment.IsSuccess.ShouldBeTrue();
        var after = await fixture.Service.GetAdminAsync(
            created.Value.Id,
            TestContext.Current.CancellationToken);
        after.Value.Lines.Single(line => line.Id == shippedLineId)
            .Status.ShouldBe(OrderLineStatus.Shipped);
        after.Value.Lines.Single(line => line.Id == unavailableLineId)
            .Status.ShouldBe(
                OrderLineStatus.Unavailable,
                "缺貨已整條退款的品項不會出貨，交運不能把它一起標成 Shipped。");
        after.Value.Status.ShouldBe(
            orderStatusBeforeDispatch,
            "訂單狀態是 ShipmentDelivered 那條路的事（ADR-025），交運不動它。");
        fixture.UnitOfWork.Saves.ShouldBe(
            1,
            "第二張出貨單沒有任何一條 line 真的改動，不該再 SaveChanges。");
    }

    [Fact(DisplayName = "#42：訂單鑑賞期屆滿完成時，已出貨的品項跟著轉 Completed")]
    public async Task Order_completion_completes_shipped_lines()
    {
        var fixture = new OrderingFixture();
        var order = await CreateReadyToShipOrderAsync(fixture);

        await fixture.Service.RecordShipmentDispatchedAsync(
            [order.Id],
            TestContext.Current.CancellationToken);
        var afterDispatch = await fixture.Service.GetAdminAsync(
            order.Id,
            TestContext.Current.CancellationToken);
        afterDispatch.Value.Lines.Single().Status.ShouldBe(
            OrderLineStatus.Shipped,
            "預購 line 收貨後是 Purchased，交運要把它推到 Shipped。");

        fixture.FulfillmentQuery.SetShipments(order.Id, ShipmentStatus.Delivered);
        await fixture.Service.RecordShipmentDeliveredAsync(
            [order.Id],
            TestContext.Current.CancellationToken);
        var timer = fixture.TimerScheduler.Scheduled.ShouldHaveSingleItem();

        fixture.Clock.UtcNow = timer.FireAt;
        await fixture.Service.ResolveAppraisalTimeoutAsync(
            order.Id,
            TestContext.Current.CancellationToken);

        var completed = await fixture.Service.GetAdminAsync(
            order.Id,
            TestContext.Current.CancellationToken);
        completed.Value.Status.ShouldBe(OrderStatus.Completed);
        completed.Value.Lines.Single().Status.ShouldBe(OrderLineStatus.Completed);
    }

    private static async Task<OrderView> CreateReadyToShipOrderAsync(OrderingFixture fixture)
    {
        var created = await fixture.Service.CreateFromCheckoutAsync(
            fixture.Checkout,
            TestContext.Current.CancellationToken);
        await fixture.Service.RecordPaymentCapturedAsync(
            created.Value.Id,
            created.Value.GrandTotal,
            TestContext.Current.CancellationToken);
        var line = created.Value.Lines.Single();
        await fixture.Service.RecordItemPurchasedAsync(
            line.Id,
            line.Quantity,
            TestContext.Current.CancellationToken);
        await fixture.Service.RecordGoodsReceivedAsync(
            line.Id,
            TestContext.Current.CancellationToken);

        var readyToShip = await fixture.Service.GetAdminAsync(
            created.Value.Id,
            TestContext.Current.CancellationToken);
        readyToShip.Value.Status.ShouldBe(OrderStatus.ReadyToShip);
        return readyToShip.Value;
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
                ConvenienceStoreName = "模擬門市（dev）",
                ConvenienceStoreAddress = "台北市模擬路 1 號",
            };
            UnitOfWork = new FakeUnitOfWork();
            Publisher = new FakeEventPublisher();
            FulfillmentQuery = new FakeFulfillmentQuery();
            TimerScheduler = new RecordingSagaTimerScheduler();
            AppraisalPeriod = TimeSpan.FromDays(7);
            Service = new OrderingApplicationService(
                new FakeOrderRepository(),
                UnitOfWork,
                Publisher,
                Pricing,
                Clock,
                new FakeCorrelation(),
                new Lazy<IFulfillmentQuery?>(() => FulfillmentQuery),
                TimerScheduler,
                AppraisalPeriod);
        }

        public CustomerId CustomerId { get; }

        public FakeClock Clock { get; }

        public FakePricing Pricing { get; }

        public CheckoutCompleted Checkout { get; }

        public FakeUnitOfWork UnitOfWork { get; }

        public FakeEventPublisher Publisher { get; }

        public FakeFulfillmentQuery FulfillmentQuery { get; }

        public RecordingSagaTimerScheduler TimerScheduler { get; }

        public TimeSpan AppraisalPeriod { get; }

        public OrderingApplicationService Service { get; }
    }
}

internal sealed class FakeFulfillmentQuery : IFulfillmentQuery
{
    private readonly Dictionary<OrderId, List<ShipmentSummary>> _byOrder = [];

    /// <summary>設定某張訂單目前掛的全部出貨單狀態；覆蓋前一次設定。</summary>
    public void SetShipments(OrderId orderId, params ShipmentStatus[] statuses) =>
        _byOrder[orderId] = statuses
            .Select(status => new ShipmentSummary(
                ShipmentId.New(),
                DeliveryMethod.ConvenienceStore,
                status,
                null,
                [orderId],
                status >= ShipmentStatus.Dispatched ? DateTimeOffset.UtcNow : null,
                status == ShipmentStatus.Delivered ? DateTimeOffset.UtcNow : null))
            .ToList();

    public Task<Result<ShipmentSummary>> GetAsync(
        ShipmentId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<ShipmentSummary>.Failure(
            "fulfillment.shipment-not-found",
            "找不到指定的出貨單。"));

    public Task<Result<IReadOnlyList<ShipmentSummary>>> GetByOrderAsync(
        OrderId orderId,
        CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<ShipmentSummary>>>(
            _byOrder.TryGetValue(orderId, out var shipments) ? shipments.ToArray() : []);

    public Task<Result<ShipmentPage>> ListAsync(
        AdminShipmentListRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<ShipmentPage>.Success(new ShipmentPage([], null)));
}

internal sealed class RecordingSagaTimerScheduler : ISagaTimerScheduler
{
    public List<(string SagaType, string SagaId, DateTimeOffset FireAt)> Scheduled { get; } = [];

    public Task<Guid> ScheduleAsync(
        string sagaType,
        string sagaId,
        DateTimeOffset fireAt,
        string payload,
        TenantId tenantId,
        CancellationToken cancellationToken)
    {
        Scheduled.Add((sagaType, sagaId, fireAt));
        return Task.FromResult(Guid.CreateVersion7());
    }

    public Task CancelAsync(Guid timerId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CancelAllForSagaAsync(
        string sagaType,
        string sagaId,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
