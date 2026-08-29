using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Fulfillment.Core;

internal sealed class FulfillmentApplicationService(
    IShipmentRepository shipments,
    IUnitOfWork unitOfWork,
    IEventPublisher eventPublisher,
    IOrderQuery orders,
    IClock clock,
    ICorrelationContext correlationContext)
    : IFulfillmentApplication, IFulfillmentQuery
{
    public async Task<Result<ShipmentSummary>> CreateAsync(
        IReadOnlyList<OrderId> orderIds,
        DeliveryMethod method,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderIds);

        foreach (var orderId in orderIds.Distinct())
        {
            var order = await orders.GetAsync(orderId, cancellationToken);
            if (order.IsFailure)
            {
                return Result<ShipmentSummary>.Failure(order.Error);
            }

            if (order.Value.Status != OrderStatus.ReadyToShip)
            {
                return Result<ShipmentSummary>.Failure(
                    "fulfillment.order-not-ready-to-ship",
                    $"訂單 {order.Value.OrderNumber} 尚未待出貨，不能建立出貨單。");
            }
        }

        var created = ShipmentAggregate.Create(
            ShipmentId.New(),
            correlationContext.TenantId,
            method,
            orderIds,
            clock.UtcNow);
        if (created.IsFailure)
        {
            return Result<ShipmentSummary>.Failure(created.Error);
        }

        shipments.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return created.Value.ToContract();
    }

    public async Task<Result<ShipmentSummary>> DispatchAsync(
        ShipmentId id,
        string trackingNumber,
        Money carrierCost,
        CancellationToken cancellationToken)
    {
        var shipment = await shipments.GetAsync(correlationContext.TenantId, id, cancellationToken);
        if (shipment is null)
        {
            return Result<ShipmentSummary>.Failure(
                "fulfillment.shipment-not-found",
                "找不到指定的出貨單。");
        }

        var transitioned = shipment.Dispatch(trackingNumber, carrierCost, clock.UtcNow);
        if (transitioned.IsFailure)
        {
            return Result<ShipmentSummary>.Failure(transitioned.Error);
        }

        if (transitioned.Value == DispatchTransition.AlreadyRecorded)
        {
            return shipment.ToContract();
        }

        await eventPublisher.PublishAsync(
            new ShipmentDispatched(
                Guid.CreateVersion7(),
                shipment.DispatchedAt!.Value,
                shipment.TenantId,
                shipment.Id,
                shipment.OrderIds,
                shipment.Method,
                trackingNumber,
                carrierCost),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return shipment.ToContract();
    }

    public async Task<Result<ShipmentSummary>> DeliverAsync(
        ShipmentId id,
        CancellationToken cancellationToken)
    {
        var shipment = await shipments.GetAsync(correlationContext.TenantId, id, cancellationToken);
        if (shipment is null)
        {
            return Result<ShipmentSummary>.Failure(
                "fulfillment.shipment-not-found",
                "找不到指定的出貨單。");
        }

        var transitioned = shipment.Deliver(clock.UtcNow);
        if (transitioned.IsFailure)
        {
            return Result<ShipmentSummary>.Failure(transitioned.Error);
        }

        if (transitioned.Value == DeliverTransition.AlreadyRecorded)
        {
            return shipment.ToContract();
        }

        await eventPublisher.PublishAsync(
            new ShipmentDelivered(
                Guid.CreateVersion7(),
                shipment.DeliveredAt!.Value,
                shipment.TenantId,
                shipment.Id,
                shipment.OrderIds),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return shipment.ToContract();
    }

    public async Task<Result<ShipmentSummary>> GetAsync(
        ShipmentId id,
        CancellationToken cancellationToken)
    {
        var shipment = await shipments.GetAsync(correlationContext.TenantId, id, cancellationToken);
        return shipment is null
            ? Result<ShipmentSummary>.Failure("fulfillment.shipment-not-found", "找不到指定的出貨單。")
            : shipment.ToContract();
    }

    public async Task<Result<IReadOnlyList<ShipmentSummary>>> GetByOrderAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        var result = await shipments.GetByOrderAsync(correlationContext.TenantId, orderId, cancellationToken);
        return result.Select(shipment => shipment.ToContract()).ToArray();
    }

    public async Task<Result<ShipmentPage>> ListAsync(
        AdminShipmentListRequest request,
        CancellationToken cancellationToken)
    {
        var page = await shipments.ListAsync(correlationContext.TenantId, request, cancellationToken);
        return new ShipmentPage(
            page.Items.Select(shipment => shipment.ToContract()).ToArray(),
            page.NextCursor?.ToString());
    }
}
