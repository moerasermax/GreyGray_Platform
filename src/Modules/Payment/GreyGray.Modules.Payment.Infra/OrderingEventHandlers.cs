using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Payment.Core;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Payment.Infra;

internal sealed class PaymentRequestedHandler : IIntegrationEventHandler<PaymentRequested>
{
    public Task HandleAsync(PaymentRequested @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (@event.Amount.Currency != Currency.TWD ||
            @event.Amount.AmountMinor <= 0 ||
            @event.Amount.AmountMinor % Currency.TWD.MinorUnitsPerUnit() != 0)
        {
            throw new InvalidOperationException("綠界付款要求必須是大於零的新台幣整數元。");
        }

        // Frozen event 只有總額，無法保留貨款／運費拆分；真正的 Payment aggregate
        // 由 /payment 的 PaymentInitiationRequest 以完整 snapshot 建立。
        return Task.CompletedTask;
    }
}

internal sealed class RefundRequestedHandler(
    IPaymentRepository payments,
    IEventPublisher eventPublisher,
    IClock clock) : IIntegrationEventHandler<RefundRequested>
{
    public async Task HandleAsync(RefundRequested @event, CancellationToken cancellationToken)
    {
        var payment = await payments.FindCapturedOrRefundedByOrderAsync(
            @event.TenantId,
            @event.OrderId,
            cancellationToken) ?? throw new InvalidOperationException(
            $"退款要求找不到訂單 {@event.OrderId} 的已收款紀錄。");

        if (@event.Destination == RefundDestination.OriginalPaymentMethod)
        {
            throw new NotSupportedException(
                "綠界原路退款尚未具備凍結的 provider API 與必要設定；不得把退款要求標成成功。");
        }

        if (@event.Destination != RefundDestination.StoredValue)
        {
            throw new InvalidOperationException($"不支援的退款去向：{@event.Destination}。");
        }

        if (@event.LineId is not null)
        {
            throw new NotSupportedException("M1a 尚未支援單一品項部分退款。");
        }

        if (!payment.RefundFully(@event.Amount))
        {
            return;
        }

        await eventPublisher.PublishAsync(
            new PaymentRefunded(
                Guid.CreateVersion7(),
                clock.UtcNow,
                @event.TenantId,
                new RefundId(@event.EventId),
                payment.Id,
                @event.OrderId,
                @event.LineId,
                payment.Provider,
                @event.Amount,
                @event.Destination),
            cancellationToken);
    }
}
