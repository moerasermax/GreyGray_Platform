using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Platform.Abstractions.Messaging;

namespace GreyGray.Modules.Ordering.Infra;

internal sealed class CheckoutCompletedHandler(IOrderingApplication ordering)
    : IIntegrationEventHandler<CheckoutCompleted>
{
    public async Task HandleAsync(
        CheckoutCompleted @event,
        CancellationToken cancellationToken)
    {
        var result = await ordering.CreateFromCheckoutAsync(@event, cancellationToken);
        ThrowIfFailure(result.IsFailure, result.Error);
    }

    private static void ThrowIfFailure(bool isFailure, GreyGray.Shared.Kernel.Error error)
    {
        if (isFailure)
        {
            throw new InvalidOperationException(
                $"CheckoutCompleted 無法建立訂單：{error.Code} {error.Message}");
        }
    }
}

internal sealed class PaymentCapturedHandler(IOrderingApplication ordering)
    : IIntegrationEventHandler<PaymentCaptured>
{
    public async Task HandleAsync(
        PaymentCaptured @event,
        CancellationToken cancellationToken)
    {
        var result = await ordering.RecordPaymentCapturedAsync(
            @event.OrderId,
            @event.Amount,
            cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"PaymentCaptured 無法更新訂單：{result.Error.Code} {result.Error.Message}");
        }
    }
}

internal sealed class PaymentFailedHandler(IOrderingApplication ordering)
    : IIntegrationEventHandler<PaymentFailed>
{
    public async Task HandleAsync(
        PaymentFailed @event,
        CancellationToken cancellationToken)
    {
        var result = await ordering.RecordPaymentFailedAsync(
            @event.OrderId,
            @event.FailureCode,
            cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"PaymentFailed 無法更新訂單：{result.Error.Code} {result.Error.Message}");
        }
    }
}

internal sealed class PaymentRefundedHandler(IOrderingApplication ordering)
    : IIntegrationEventHandler<PaymentRefunded>
{
    public async Task HandleAsync(
        PaymentRefunded @event,
        CancellationToken cancellationToken)
    {
        var result = await ordering.RecordPaymentRefundedAsync(
            @event.OrderId,
            @event.Amount,
            cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"PaymentRefunded 無法更新訂單：{result.Error.Code} {result.Error.Message}");
        }
    }
}

internal sealed class ItemPurchasedHandler(IOrderingApplication ordering)
    : IIntegrationEventHandler<ItemPurchased>
{
    public async Task HandleAsync(
        ItemPurchased @event,
        CancellationToken cancellationToken)
    {
        var result = await ordering.RecordItemPurchasedAsync(
            @event.OrderLineId,
            @event.Quantity,
            cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"ItemPurchased 無法更新訂單：{result.Error.Code} {result.Error.Message}");
        }
    }
}

internal sealed class ShipmentDeliveredHandler(IOrderingShipmentDelivery ordering)
    : IIntegrationEventHandler<ShipmentDelivered>
{
    public async Task HandleAsync(
        ShipmentDelivered @event,
        CancellationToken cancellationToken)
    {
        var result = await ordering.RecordShipmentDeliveredAsync(
            @event.OrderIds,
            cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"ShipmentDelivered 無法更新訂單：{result.Error.Code} {result.Error.Message}");
        }
    }
}
