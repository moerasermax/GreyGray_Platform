using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Ordering.Core;

internal sealed class OrderingApplicationService(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IEventPublisher eventPublisher,
    IPricingQuotation pricing,
    IClock clock,
    ICorrelationContext correlationContext)
    : IOrderingApplication, IOrderingGoodsReceipt, IOrderQuery
{
    public async Task<Result<OrderView>> CreateFromCheckoutAsync(
        CheckoutCompleted checkout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkout);

        var existing = await orders.GetByCheckoutAsync(
            checkout.TenantId,
            checkout.CartId,
            cancellationToken);
        if (existing is not null)
        {
            return StringComparer.Ordinal.Equals(
                    existing.CheckoutIdempotencyKey,
                    checkout.IdempotencyKey)
                ? existing.ToView()
                : Result<OrderView>.Failure(
                    "ordering.checkout-already-processed",
                    "這個購物車已用另一把冪等鍵建立訂單。");
        }

        var snapshot = await pricing.GetSnapshotAsync(
            checkout.PricingSnapshotId,
            cancellationToken);
        if (snapshot.IsFailure)
        {
            return Result<OrderView>.Failure(snapshot.Error);
        }

        var placed = Order.Place(OrderId.New(), checkout, snapshot.Value, clock.UtcNow);
        if (placed.IsFailure)
        {
            return Result<OrderView>.Failure(placed.Error);
        }

        var order = placed.Value;
        orders.Add(order);
        await eventPublisher.PublishAsync(ToOrderPlaced(order), cancellationToken);
        await eventPublisher.PublishAsync(
            new PaymentRequested(
                Guid.CreateVersion7(),
                order.PlacedAt,
                order.TenantId,
                order.Id,
                order.CustomerId,
                order.GrandTotal,
                checkout.IdempotencyKey),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return order.ToView();
    }

    public async Task<Result<OrderPage<OrderView>>> ListCustomerAsync(
        CustomerOrderListRequest request,
        CancellationToken cancellationToken)
    {
        var limitError = ValidateLimit(request.Limit);
        if (limitError is not null)
        {
            return Result<OrderPage<OrderView>>.Failure(limitError);
        }

        var page = await orders.ListCustomerAsync(
            correlationContext.TenantId,
            request,
            cancellationToken);
        return ToPage(page);
    }

    public async Task<Result<OrderView>> GetCustomerAsync(
        CustomerId customerId,
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(
            correlationContext.TenantId,
            orderId,
            cancellationToken);
        return order is null || order.CustomerId != customerId
            ? OrderNotFound<OrderView>()
            : order.ToView();
    }

    public async Task<Result<OrderView>> CancelCustomerAsync(
        CustomerId customerId,
        OrderId orderId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(
            correlationContext.TenantId,
            orderId,
            cancellationToken);
        if (order is null || order.CustomerId != customerId)
        {
            return OrderNotFound<OrderView>();
        }

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "客戶取消" : reason.Trim();
        if (normalizedReason.Length > 200)
        {
            return Result<OrderView>.Failure(
                "ordering.cancel-reason-invalid",
                "取消原因不得超過 200 個字元。");
        }
        var cancelled = order.CancelByCustomer(normalizedReason, clock.UtcNow);
        if (cancelled.IsFailure)
        {
            return Result<OrderView>.Failure(cancelled.Error);
        }

        await PublishCancellationAsync(
            order,
            normalizedReason,
            cancelled.Value,
            RefundDestination.OriginalPaymentMethod,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return order.ToView();
    }

    public async Task<Result<OrderPage<OrderView>>> ListAdminAsync(
        AdminOrderListRequest request,
        CancellationToken cancellationToken)
    {
        var limitError = ValidateLimit(request.Limit);
        if (limitError is not null)
        {
            return Result<OrderPage<OrderView>>.Failure(limitError);
        }

        var page = await orders.ListAdminAsync(
            correlationContext.TenantId,
            request,
            cancellationToken);
        return ToPage(page);
    }

    public async Task<Result<OrderView>> GetAdminAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(
            correlationContext.TenantId,
            orderId,
            cancellationToken);
        return order is null ? OrderNotFound<OrderView>() : order.ToView();
    }

    public async Task<Result<OrderView>> CancelAdminAsync(
        OrderId orderId,
        string reason,
        RefundDestination refundTo,
        CancellationToken cancellationToken)
    {
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 200)
        {
            return Result<OrderView>.Failure(
                "ordering.cancel-reason-invalid",
                "取消原因必須是 1 到 200 個字元。");
        }

        var order = await orders.GetAsync(
            correlationContext.TenantId,
            orderId,
            cancellationToken);
        if (order is null)
        {
            return OrderNotFound<OrderView>();
        }

        var cancelled = order.CancelByAdmin(normalizedReason, clock.UtcNow);
        if (cancelled.IsFailure)
        {
            return Result<OrderView>.Failure(cancelled.Error);
        }

        await PublishCancellationAsync(
            order,
            normalizedReason,
            cancelled.Value,
            refundTo,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return order.ToView();
    }

    public async Task<Result<OrderView>> CancelLineAsync(
        OrderId orderId,
        OrderLineId lineId,
        string reason,
        RefundDestination refundTo,
        CancellationToken cancellationToken)
    {
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 200)
        {
            return Result<OrderView>.Failure(
                "ordering.cancel-reason-invalid",
                "取消原因必須是 1 到 200 個字元。");
        }

        var order = await orders.GetAsync(
            correlationContext.TenantId,
            orderId,
            cancellationToken);
        if (order is null)
        {
            return OrderNotFound<OrderView>();
        }

        var cancelled = order.CancelLineByAdmin(lineId);
        if (cancelled.IsFailure)
        {
            return Result<OrderView>.Failure(cancelled.Error);
        }

        var occurredAt = clock.UtcNow;
        await eventPublisher.PublishAsync(
            new OrderLineCancelled(
                Guid.CreateVersion7(),
                occurredAt,
                order.TenantId,
                order.Id,
                lineId,
                normalizedReason,
                cancelled.Value,
                refundTo),
            cancellationToken);

        if (order.PaidAmount is not null && !cancelled.Value.IsZero)
        {
            await eventPublisher.PublishAsync(
                new RefundRequested(
                    Guid.CreateVersion7(),
                    occurredAt,
                    order.TenantId,
                    order.Id,
                    lineId,
                    cancelled.Value,
                    refundTo,
                    normalizedReason),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return order.ToView();
    }

    public async Task<Result> RecordPaymentCapturedAsync(
        OrderId orderId,
        Money amount,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(
            correlationContext.TenantId,
            orderId,
            cancellationToken);
        if (order is null)
        {
            return Result.Failure("ordering.order-not-found", "找不到訂單。");
        }

        var captured = order.CapturePayment(amount);
        if (captured.IsFailure)
        {
            return Result.Failure(captured.Error);
        }

        if (captured.Value == PaymentCaptureTransition.AlreadyRecorded)
        {
            return Result.Success();
        }

        var occurredAt = clock.UtcNow;
        await eventPublisher.PublishAsync(
            new OrderPaid(
                Guid.CreateVersion7(),
                occurredAt,
                order.TenantId,
                order.Id,
                amount),
            cancellationToken);

        if (captured.Value == PaymentCaptureTransition.ReadyToShip)
        {
            await eventPublisher.PublishAsync(
                new OrderReadyToShip(
                    Guid.CreateVersion7(),
                    occurredAt,
                    order.TenantId,
                    order.Id,
                    order.DeliveryMethod,
                    order.ShippingAddressId),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RecordPaymentFailedAsync(
        OrderId orderId,
        string failureCode,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(
            correlationContext.TenantId,
            orderId,
            cancellationToken);
        if (order is null)
        {
            return Result.Failure("ordering.order-not-found", "找不到訂單。");
        }

        var result = order.RecordPaymentFailure(failureCode);
        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RecordPaymentRefundedAsync(
        OrderId orderId,
        Money amount,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(
            correlationContext.TenantId,
            orderId,
            cancellationToken);
        if (order is null)
        {
            return Result.Failure("ordering.order-not-found", "找不到訂單。");
        }

        var result = order.RecordRefund(amount);
        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RecordItemPurchasedAsync(
        OrderLineId orderLineId,
        int quantityPurchased,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetByLineAsync(
            correlationContext.TenantId,
            orderLineId,
            cancellationToken);
        if (order is null)
        {
            return Result.Failure("ordering.order-line-not-found", "找不到訂單品項。");
        }

        var result = order.RecordItemPurchased(orderLineId, quantityPurchased);
        if (result.IsFailure)
        {
            return Result.Failure(result.Error);
        }

        if (result.Value == PurchaseLineTransition.Recorded)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> RecordGoodsReceivedAsync(
        OrderLineId orderLineId,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetByLineAsync(
            correlationContext.TenantId,
            orderLineId,
            cancellationToken);
        if (order is null)
        {
            return Result.Failure("ordering.order-line-not-found", "找不到訂單品項。");
        }

        var occurredAt = clock.UtcNow;
        var result = order.RecordGoodsReceived(orderLineId, occurredAt);
        if (result.IsFailure)
        {
            return Result.Failure(result.Error);
        }

        if (result.Value == GoodsReceivedTransition.AlreadyRecorded)
        {
            return Result.Success();
        }

        if (result.Value == GoodsReceivedTransition.ReadyToShip)
        {
            await eventPublisher.PublishAsync(
                new OrderReadyToShip(
                    Guid.CreateVersion7(),
                    occurredAt,
                    order.TenantId,
                    order.Id,
                    order.DeliveryMethod,
                    order.ShippingAddressId),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<OrderView>> GetAsync(
        OrderId id,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(correlationContext.TenantId, id, cancellationToken);
        return order is null ? OrderNotFound<OrderView>() : order.ToView();
    }

    public async Task<Result<IReadOnlyList<OrderView>>> GetByCampaignAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken)
    {
        var found = await orders.GetByCampaignAsync(
            correlationContext.TenantId,
            campaignId,
            cancellationToken);
        return found.Select(order => order.ToView()).ToArray();
    }

    private async Task PublishCancellationAsync(
        Order order,
        string reason,
        Money refundAmount,
        RefundDestination refundTo,
        CancellationToken cancellationToken)
    {
        var occurredAt = clock.UtcNow;
        await eventPublisher.PublishAsync(
            new OrderCancelled(
                Guid.CreateVersion7(),
                occurredAt,
                order.TenantId,
                order.Id,
                reason,
                refundAmount,
                refundTo),
            cancellationToken);

        if (!refundAmount.IsZero)
        {
            await eventPublisher.PublishAsync(
                new RefundRequested(
                    Guid.CreateVersion7(),
                    occurredAt,
                    order.TenantId,
                    order.Id,
                    null,
                    refundAmount,
                    refundTo,
                    reason),
                cancellationToken);
        }
    }

    private static OrderPlaced ToOrderPlaced(Order order) =>
        new(
            Guid.CreateVersion7(),
            order.PlacedAt,
            order.TenantId,
            order.Id,
            order.CustomerId,
            order.Source,
            order.ShippingPolicy,
            order.PricingSnapshotId,
            order.GoodsTotal,
            order.ShippingFee,
            order.Lines.Select(line => new OrderPlacedLine(
                line.Id,
                line.SkuId,
                line.Mode,
                line.CampaignId,
                line.CampaignOfferId,
                line.Quantity,
                line.UnitPrice)).ToArray());

    private static OrderPage<OrderView> ToPage(OrderQueryPage page) =>
        new(
            page.Items.Select(order => order.ToView()).ToArray(),
            page.NextCursor?.ToString());

    private static Error? ValidateLimit(int limit) =>
        limit is < 1 or > 100
            ? new Error("ordering.invalid-limit", "每頁筆數必須介於 1 到 100。")
            : null;

    private static Result<T> OrderNotFound<T>() =>
        Result<T>.Failure("ordering.order-not-found", "找不到訂單。");
}
