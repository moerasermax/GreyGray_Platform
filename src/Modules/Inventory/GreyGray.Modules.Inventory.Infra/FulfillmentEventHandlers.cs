using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Inventory.Core;
using GreyGray.Platform.Abstractions.Messaging;

namespace GreyGray.Modules.Inventory.Infra;

/// <summary>
/// 交運出庫（#43）。貨在<b>交運</b>那一刻離開倉庫，所以出庫掛在
/// <see cref="ShipmentDispatched"/> 而不是簽收——`docs/02` §5 分錄表第 ⑦ 階段
/// 兩筆（銷貨成本結轉、運費成本）也都在出貨這一段。
/// </summary>
/// <remarks>
/// 一張出貨單可以合併多張訂單（<see cref="ShipmentDispatched.OrderIds"/> 是陣列），
/// 反過來一張訂單也可能拆進多張出貨單。所以這裡逐一處理每個 orderId，
/// 而<b>冪等不能靠 <c>platform.processed_message</c></b>：它的去重範圍是 (event, handler)，
/// 擋得住同一個事件重放，擋不住第二張出貨單又帶到同一個 orderId。
/// 真正的守衛在 <c>StockReservationService.ConsumeAsync</c> 裡的 reservation 狀態。
/// </remarks>
internal sealed class ShipmentDispatchedInventoryHandler(StockReservationService reservations)
    : IIntegrationEventHandler<ShipmentDispatched>
{
    public async Task HandleAsync(
        ShipmentDispatched @event,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        foreach (var orderId in @event.OrderIds.Distinct())
        {
            var result = await reservations.ConsumeAsync(
                StockReservationPlan.ForOrder(orderId),
                orderId.ToString(),
                cancellationToken);
            if (result.IsFailure)
            {
                throw new InvalidOperationException(
                    $"ShipmentDispatched 無法為訂單 {orderId} 出庫：" +
                    $"{result.Error.Code} {result.Error.Message}");
            }
        }
    }
}
