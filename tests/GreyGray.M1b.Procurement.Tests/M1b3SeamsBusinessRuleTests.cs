using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Campaign.Core;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Modules.Procurement.Core;
using GreyGray.Shared.Kernel;
using Shouldly;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.M1b.Procurement.Tests;

public sealed class M1b3SeamsBusinessRuleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 29, 8, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Procurement 只允許 Purchased 品項帶回，失敗以 Result 回傳")]
    public void Procurement_rejects_receipt_before_purchase_with_result()
    {
        var item = PurchaseItemAggregate.Create(
            PurchaseItemId.New(),
            TenantId.Default,
            CampaignId.New(),
            SkuId.New(),
            OrderLineId.New(),
            1,
            Money.OfMajor(350, Currency.TWD),
            Now).Value;

        var result = item.MarkReceived(Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("procurement.purchase-item-not-purchased");
        item.ReceivedAt.ShouldBeNull();
    }

    [Fact(DisplayName = "已取消 Campaign 仍可登錄真實旅程損失，且不同幣別以 Result 拒絕")]
    public void Cancelled_campaign_keeps_trip_cost_lifecycle_independent()
    {
        var campaign = CampaignAggregate.CreateDraft(
            CampaignId.New(),
            TenantId.Default,
            new CampaignDraftInput(
                "取消旅程",
                "Tokyo",
                new DateOnly(2026, 9, 10),
                new DateOnly(2026, 9, 12),
                Now.AddDays(5),
                null,
                null),
            Now).Value;
        campaign.AddOffer(
            new CampaignOfferInput(
                SkuId.New(),
                Money.OfMajor(500, Currency.TWD),
                Money.OfMajor(350, Currency.TWD)),
            Now).IsSuccess.ShouldBeTrue();
        campaign.Publish(Now).IsSuccess.ShouldBeTrue();
        campaign.Cancel("颱風取消", Now).IsSuccess.ShouldBeTrue();

        var recorded = campaign.RecordTripCost(
            new TripCostInput(
                TripCostId.New(),
                TripCostKind.Airfare,
                Money.OfMajor(12_000, Currency.TWD),
                "不可退票"),
            Now);
        var mismatch = campaign.RecordTripCost(
            new TripCostInput(
                TripCostId.New(),
                TripCostKind.LocalTransport,
                Money.OfMajor(500, Currency.JPY),
                null),
            Now);

        recorded.IsSuccess.ShouldBeTrue();
        campaign.Status.ShouldBe(CampaignStatus.Cancelled);
        campaign.TripCostTotal.ShouldBe(Money.OfMajor(12_000, Currency.TWD));
        mismatch.IsFailure.ShouldBeTrue();
        mismatch.Error.Code.ShouldBe("campaign.trip-cost-currency-mismatch");
    }

    [Fact(DisplayName = "Ordering 只允許 Purchased 預購 line 收貨，現貨與未買到均回 Result")]
    public void Ordering_rejects_non_purchased_and_stock_receipts_with_result()
    {
        var preorder = M1b3SeamsPostgresTests.CreateUnpaidOrder(preorderCount: 1);
        preorder.CapturePayment(preorder.GrandTotal).IsSuccess.ShouldBeTrue();
        var preorderLine = preorder.Lines.Single();
        var beforePurchase = preorder.RecordGoodsReceived(preorderLine.Id, Now);

        var stock = M1b3SeamsPostgresTests.CreateUnpaidOrder(preorderCount: 0);
        stock.CapturePayment(stock.GrandTotal).IsSuccess.ShouldBeTrue();
        var stockLine = stock.Lines.Single(line => line.Mode == FulfillmentMode.Stock);
        var stockReceipt = stock.RecordGoodsReceived(stockLine.Id, Now);

        beforePurchase.IsFailure.ShouldBeTrue();
        beforePurchase.Error.Code.ShouldBe("ordering.order-line-not-purchased");
        stockReceipt.IsFailure.ShouldBeTrue();
        stockReceipt.Error.Code.ShouldBe("ordering.stock-line-does-not-receive-goods");
    }
}
