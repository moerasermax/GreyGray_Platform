using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Abstractions.Saga;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Ordering.Core;

internal sealed class OrderingApplicationService(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IEventPublisher eventPublisher,
    IPricingQuotation pricing,
    IClock clock,
    ICorrelationContext correlationContext,
    Lazy<IFulfillmentQuery?>? fulfillmentQuery = null,
    ISagaTimerScheduler? timerScheduler = null,
    TimeSpan appraisalPeriod = default)
    : IOrderingApplication, IOrderingGoodsReceipt, IOrderQuery, IOrderingShipmentDelivery
{
    /// <summary>鑑賞期 Saga timer 的 saga type（ADR-025）。</summary>
    internal const string AppraisalSagaType = "ordering.appraisal-period";

    /// <summary>
    /// 儲值金退款要到 M3 才開放（ADR-023 決定二／ADR-024 後半）。整條退款流程已經實作完成，
    /// 缺的只有客人主動加值入口與到期規則；M3 開放時只需把這個常數改為 <c>true</c>。
    /// </summary>
    private const bool StoredValueRefundOpen = false;

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

        var storedValueGuard = GuardStoredValueRefund(refundTo);
        if (storedValueGuard is not null)
        {
            return Result<OrderView>.Failure(storedValueGuard);
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

        var storedValueGuard = GuardStoredValueRefund(refundTo);
        if (storedValueGuard is not null)
        {
            return Result<OrderView>.Failure(storedValueGuard);
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

    public async Task<Result<OrderView>> RefundLineShortfallAsync(
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

        var storedValueGuard = GuardStoredValueRefund(refundTo);
        if (storedValueGuard is not null)
        {
            return Result<OrderView>.Failure(storedValueGuard);
        }

        var order = await orders.GetAsync(
            correlationContext.TenantId,
            orderId,
            cancellationToken);
        if (order is null)
        {
            return OrderNotFound<OrderView>();
        }

        var refunded = order.RefundLineShortfallByAdmin(lineId);
        if (refunded.IsFailure)
        {
            return Result<OrderView>.Failure(refunded.Error);
        }

        // 重用既有的 RefundRequested 事件與下游 Payment／Ledger 消費者——
        // 短缺退款不是另一套退款機制（ADR-026），不新增事件契約。
        if (order.PaidAmount is not null && !refunded.Value.IsZero)
        {
            await eventPublisher.PublishAsync(
                new RefundRequested(
                    Guid.CreateVersion7(),
                    clock.UtcNow,
                    order.TenantId,
                    order.Id,
                    lineId,
                    refunded.Value,
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

    /// <summary>
    /// Fulfillment 出貨單簽收後呼叫。一張訂單可能對應多個出貨單（N:M），
    /// 必須全部簽收才起算鑑賞期，所以每次都要重新問 Fulfillment 那張訂單目前掛的全部出貨單。
    /// </summary>
    public async Task<Result> RecordShipmentDeliveredAsync(
        IReadOnlyList<OrderId> orderIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderIds);
        ArgumentNullException.ThrowIfNull(fulfillmentQuery);
        ArgumentNullException.ThrowIfNull(timerScheduler);

        // 延遲解析（ADR-025 循環相依修法，docs/24 §0）：只有走到這裡才真的觸發
        // IFulfillmentQuery 的 GetService，不會在 OrderingApplicationService 建構時就解析。
        var resolvedFulfillmentQuery = fulfillmentQuery.Value;
        if (resolvedFulfillmentQuery is null)
        {
            // 缺的是「模組沒掛」，不是「參數傳了 null」——訊息要讓看 log 的人直接知道
            // 要去改哪個 Host 的組合根（#41：Worker 從來沒掛 Fulfillment，
            // 出貨單簽收事件因此連續重試失敗，訂單永遠停在「準備出貨」）。
            throw new InvalidOperationException(
                "這個 Host 登記了 ShipmentDeliveredHandler，卻沒有掛 Fulfillment 模組"
                + "（IFulfillmentQuery 解析不到）；Worker 的模組清單在 "
                + "GreyGray.Worker/WorkerModules.cs 的 AddWorkerModules。");
        }

        var scheduled = false;
        foreach (var orderId in orderIds.Distinct())
        {
            var order = await orders.GetAsync(correlationContext.TenantId, orderId, cancellationToken);
            if (order is null)
            {
                continue;
            }

            var shipments = await resolvedFulfillmentQuery.GetByOrderAsync(orderId, cancellationToken);
            if (shipments.IsFailure)
            {
                return Result.Failure(shipments.Error);
            }

            var allDelivered = shipments.Value.Count > 0
                && shipments.Value.All(shipment => shipment.Status == ShipmentStatus.Delivered);
            if (!allDelivered)
            {
                continue;
            }

            var appraisalDueAt = clock.UtcNow + appraisalPeriod;
            var transition = order.RecordAllShipmentsDelivered(appraisalDueAt);
            if (transition != ShipmentDeliveryTransition.AppraisalScheduled)
            {
                continue;
            }

            await timerScheduler.ScheduleAsync(
                AppraisalSagaType,
                order.Id.ToString(),
                appraisalDueAt,
                "{}",
                order.TenantId,
                cancellationToken);
            scheduled = true;
        }

        if (scheduled)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>鑑賞期 Saga timer 屆滿時呼叫；孤兒 timer（訂單不存在或狀態不對）安靜 no-op。</summary>
    internal async Task ResolveAppraisalTimeoutAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(correlationContext.TenantId, orderId, cancellationToken);
        if (order is null)
        {
            return;
        }

        var transition = order.CompleteAfterAppraisal();
        if (transition != AppraisalTimeoutTransition.Completed)
        {
            return;
        }

        await eventPublisher.PublishAsync(
            new OrderCompleted(
                Guid.CreateVersion7(),
                clock.UtcNow,
                order.TenantId,
                order.Id,
                order.GoodsTotal,
                order.ShippingFee),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static Error? GuardStoredValueRefund(RefundDestination refundTo) =>
        refundTo == RefundDestination.StoredValue && !StoredValueRefundOpen
            ? new Error("ordering.stored-value-refund-not-available", "儲值金退款要到 M3 才開放，請改選原路退款。")
            : null;

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
