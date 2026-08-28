using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Inventory.Core;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.Inventory.Tests;

public sealed class StockReservationPlanTests
{
    [Fact(DisplayName = "reservation plan 會正規化 key、合併 SKU 並固定鎖定順序")]
    public void Plan_normalizes_aggregates_and_sorts()
    {
        var first = new SkuId(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        var second = new SkuId(Guid.Parse("00000000-0000-0000-0000-000000000002"));

        var result = StockReservationPlan.Create(
            "  order-1  ",
            [(first, 2), (second, 3), (first, 4)]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ReservationKey.ShouldBe("order-1");
        result.Value.Lines.Select(line => line.SkuId).ShouldBe([second, first]);
        result.Value.Lines.Select(line => line.Quantity).ShouldBe([3, 6]);
    }

    [Theory(DisplayName = "reservation plan 拒絕空 key 與非正數數量")]
    [InlineData("", 1)]
    [InlineData("order-1", 0)]
    [InlineData("order-1", -1)]
    public void Plan_rejects_invalid_input(string key, int quantity)
    {
        var result = StockReservationPlan.Create(key, [(SkuId.New(), quantity)]);

        result.IsFailure.ShouldBeTrue();
    }
}
