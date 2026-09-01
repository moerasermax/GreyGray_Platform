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
    /// <summary>
    /// 建立出貨單。<b>模組自己有一層冪等</b>：BFF 的冪等鍵在收尾階段出錯時會被 abandon，
    /// 店員用同一把鍵重送就會建出第二張出貨單，而重複那張會被撿貨、被交運、
    /// 物流成本重複入帳（見 <c>docs/32</c> §0）。所以這裡比照 Checkout 與 Ordering，
    /// 把鍵存進聚合並以它為準判斷「這是重播還是新的一張」。
    /// </summary>
    public async Task<Result<ShipmentSummary>> CreateAsync(
        IReadOnlyList<OrderId> orderIds,
        DeliveryMethod method,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderIds);

        var key = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 255)
        {
            return Result<ShipmentSummary>.Failure(
                "platform.idempotency-key-required",
                "建立出貨單必須提供 1 到 255 字元的 Idempotency-Key。");
        }

        // ★ 這一段一定要排在下面的「每張訂單都要是 ReadyToShip」守衛之前。
        // 出貨單建好之後訂單狀態會被下游推走（ShipmentDispatched → Ordering 轉狀態），
        // 先跑守衛的話重播會被 fulfillment.order-not-ready-to-ship 擋成失敗，
        // 「重試永遠回不了成功」——那正是 #22 (A″) 的死路，不要在這裡複製一個。
        var existing = await shipments.GetByCreationKeyAsync(
            correlationContext.TenantId,
            key,
            cancellationToken);
        if (existing is not null)
        {
            return existing.Method == method
                && existing.OrderIds.ToHashSet().SetEquals(orderIds)
                ? existing.ToContract()
                : Result<ShipmentSummary>.Failure(
                    "fulfillment.idempotency-key-reused",
                    "這把 Idempotency-Key 已經建立過內容不同的出貨單。");
        }

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
            key,
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
