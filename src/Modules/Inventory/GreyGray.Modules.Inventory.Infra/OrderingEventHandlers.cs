using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Inventory.Core;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Platform.Abstractions.Messaging;

namespace GreyGray.Modules.Inventory.Infra;

internal sealed class OrderPlacedInventoryHandler(StockReservationService reservations)
    : IIntegrationEventHandler<OrderPlaced>
{
    public async Task HandleAsync(
        OrderPlaced @event,
        CancellationToken cancellationToken)
    {
        var stockLines = @event.Lines
            .Where(line => line.Mode == FulfillmentMode.Stock)
            .Select(line => (line.SkuId, line.Quantity))
            .ToArray();
        if (stockLines.Length == 0)
        {
            return;
        }

        var result = await reservations.ReserveAsync(
            StockReservationPlan.ForOrder(@event.OrderId),
            stockLines,
            cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"OrderPlaced 無法保留現貨：{result.Error.Code} {result.Error.Message}");
        }
    }
}

internal sealed class OrderCancelledInventoryHandler(StockReservationService reservations)
    : IIntegrationEventHandler<OrderCancelled>
{
    public async Task HandleAsync(
        OrderCancelled @event,
        CancellationToken cancellationToken)
    {
        var result = await reservations.ReleaseByKeyAsync(
            StockReservationPlan.ForOrder(@event.OrderId),
            cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"OrderCancelled 無法釋放現貨：{result.Error.Code} {result.Error.Message}");
        }
    }
}
