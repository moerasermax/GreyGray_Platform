using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Fulfillment.Core;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Shared.Kernel;
using Shouldly;
using Xunit;

namespace GreyGray.M1b.Fulfillment.Tests;

public sealed class ShipmentAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 3, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "一個包裹可以含同一位客人的多張訂單（N:M 的 N 端）")]
    public void Create_accepts_multiple_orders_in_one_shipment()
    {
        var orderIds = new[] { OrderId.New(), OrderId.New() };

        var shipment = ShipmentAggregate.Create(
            ShipmentId.New(),
            TenantId.Default,
            DeliveryMethod.HomeDelivery,
            orderIds,
            Now);

        shipment.IsSuccess.ShouldBeTrue();
        shipment.Value.Status.ShouldBe(ShipmentStatus.Draft);
        shipment.Value.OrderIds.ShouldBe(orderIds, ignoreOrder: true);
    }

    [Fact(DisplayName = "同一張訂單可以拆進兩個不同的出貨單（N:M 的 M 端）")]
    public void Same_order_can_belong_to_two_different_shipments()
    {
        var orderId = OrderId.New();

        var first = ShipmentAggregate.Create(
            ShipmentId.New(), TenantId.Default, DeliveryMethod.ConvenienceStore, [orderId], Now);
        var second = ShipmentAggregate.Create(
            ShipmentId.New(), TenantId.Default, DeliveryMethod.HomeDelivery, [orderId], Now);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        first.Value.Id.ShouldNotBe(second.Value.Id);
        first.Value.OrderIds.ShouldContain(orderId);
        second.Value.OrderIds.ShouldContain(orderId);
    }

    [Fact(DisplayName = "出貨單至少要有一張訂單")]
    public void Create_rejects_empty_order_list()
    {
        var result = ShipmentAggregate.Create(
            ShipmentId.New(), TenantId.Default, DeliveryMethod.SelfPickup, [], Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("fulfillment.order-ids-required");
    }

    [Fact(DisplayName = "同一張訂單不能在同一張出貨單裡重複出現")]
    public void Create_rejects_duplicate_order_id()
    {
        var orderId = OrderId.New();

        var result = ShipmentAggregate.Create(
            ShipmentId.New(), TenantId.Default, DeliveryMethod.HomeDelivery, [orderId, orderId], Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("fulfillment.duplicate-order-id");
    }

    [Fact(DisplayName = "交運成功後 carrierCost 與 shippingFee 是分開的兩個數字")]
    public void Dispatch_records_carrier_cost_and_tracking_number()
    {
        var shipment = CreateDraft();
        var carrierCost = Money.OfMajor(150, Currency.TWD);

        var transitioned = shipment.Dispatch("TRK-001", carrierCost, Now);

        transitioned.IsSuccess.ShouldBeTrue();
        transitioned.Value.ShouldBe(DispatchTransition.Recorded);
        shipment.Status.ShouldBe(ShipmentStatus.Dispatched);
        shipment.TrackingNumber.ShouldBe("TRK-001");
        shipment.CarrierCost.ShouldBe(carrierCost);
        shipment.DispatchedAt.ShouldBe(Now);
    }

    [Fact(DisplayName = "交運重送同樣的內容是冪等的")]
    public void Dispatch_is_idempotent_for_identical_replay()
    {
        var shipment = CreateDraft();
        var carrierCost = Money.OfMajor(150, Currency.TWD);
        shipment.Dispatch("TRK-001", carrierCost, Now).IsSuccess.ShouldBeTrue();

        var replay = shipment.Dispatch("TRK-001", carrierCost, Now.AddMinutes(5));

        replay.IsSuccess.ShouldBeTrue();
        replay.Value.ShouldBe(DispatchTransition.AlreadyRecorded);
        shipment.DispatchedAt.ShouldBe(Now);
    }

    [Fact(DisplayName = "交運重送但內容不同要回業務失敗")]
    public void Dispatch_rejects_conflicting_replay()
    {
        var shipment = CreateDraft();
        shipment.Dispatch("TRK-001", Money.OfMajor(150, Currency.TWD), Now).IsSuccess.ShouldBeTrue();

        var conflicting = shipment.Dispatch("TRK-002", Money.OfMajor(150, Currency.TWD), Now);

        conflicting.IsFailure.ShouldBeTrue();
        conflicting.Error.Code.ShouldBe("fulfillment.dispatch-already-recorded");
    }

    [Fact(DisplayName = "物流成本不得為負數")]
    public void Dispatch_rejects_negative_carrier_cost()
    {
        var shipment = CreateDraft();

        var result = shipment.Dispatch("TRK-001", Money.OfMajor(-1, Currency.TWD), Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("fulfillment.invalid-carrier-cost");
    }

    [Fact(DisplayName = "還沒交運不能簽收")]
    public void Deliver_rejects_shipment_not_yet_dispatched()
    {
        var shipment = CreateDraft();

        var result = shipment.Deliver(Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("fulfillment.shipment-not-deliverable");
    }

    [Fact(DisplayName = "簽收成功後狀態轉為 Delivered，重送冪等")]
    public void Deliver_transitions_and_replay_is_idempotent()
    {
        var shipment = CreateDraft();
        shipment.Dispatch("TRK-001", Money.OfMajor(150, Currency.TWD), Now).IsSuccess.ShouldBeTrue();

        var delivered = shipment.Deliver(Now.AddHours(24));
        delivered.IsSuccess.ShouldBeTrue();
        delivered.Value.ShouldBe(DeliverTransition.Recorded);
        shipment.Status.ShouldBe(ShipmentStatus.Delivered);

        var replay = shipment.Deliver(Now.AddHours(48));
        replay.IsSuccess.ShouldBeTrue();
        replay.Value.ShouldBe(DeliverTransition.AlreadyRecorded);
        shipment.DeliveredAt.ShouldBe(Now.AddHours(24));
    }

    private static ShipmentAggregate CreateDraft() =>
        ShipmentAggregate.Create(
            ShipmentId.New(),
            TenantId.Default,
            DeliveryMethod.HomeDelivery,
            [OrderId.New()],
            Now).Value;
}
