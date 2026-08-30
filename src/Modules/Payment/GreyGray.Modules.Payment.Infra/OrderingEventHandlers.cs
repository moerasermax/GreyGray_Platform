using System.Text.Json;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Payment.Core;
using GreyGray.Platform.Abstractions.Audit;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using GreyGray.Shared.Kernel.Json;
using PaymentEntity = GreyGray.Modules.Payment.Core.Payment;

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
    IEcpayGateway ecpay,
    IClock clock,
    IAuditWriter? auditWriter) : IIntegrationEventHandler<RefundRequested>
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
            await HandleOriginalPaymentMethodAsync(@event, payment, cancellationToken);
            return;
        }

        if (@event.Destination != RefundDestination.StoredValue)
        {
            throw new InvalidOperationException($"不支援的退款去向：{@event.Destination}。");
        }

        if (!payment.Refund(@event.Amount))
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

    /// <summary>
    /// 綠界原路退刷（ADR-024）。<b>累計上限要先在本地驗過才能呼叫綠界</b>——
    /// 若順序反過來，綠界退了錢但本地 <see cref="PaymentEntity.Refund"/> 事後才拒絕，
    /// 交易回滾會讓這筆訊息被重送，下一次又會再呼叫一次綠界，變成真的重複退款。
    /// </summary>
    private async Task HandleOriginalPaymentMethodAsync(
        RefundRequested @event,
        PaymentEntity payment,
        CancellationToken cancellationToken)
    {
        var projectedRefundedMinor = checked(payment.RefundedAmountMinor + @event.Amount.AmountMinor);
        if (@event.Amount.Currency != Currency.TWD ||
            @event.Amount.IsNegative ||
            @event.Amount.IsZero ||
            projectedRefundedMinor > payment.Amount.AmountMinor)
        {
            await WriteAuditAsync(
                @event,
                payment,
                succeeded: false,
                rtnCode: null,
                rtnMsg: "退款金額超過原付款可退餘額或金額無效，未呼叫綠界。",
                rawResponse: null,
                cancellationToken);
            return;
        }

        if (payment.ProviderTransactionId is null)
        {
            // 不該發生：Captured／PartiallyRefunded 狀態下一定已經有綠界交易編號。
            throw new InvalidOperationException(
                $"付款 {payment.Id} 已收款但缺少綠界交易編號，無法發動退刷。");
        }

        var refundResult = await ecpay.RequestRefundAsync(
            payment.MerchantTradeNo,
            payment.ProviderTransactionId,
            @event.Amount,
            cancellationToken);

        await WriteAuditAsync(
            @event,
            payment,
            refundResult.Succeeded,
            refundResult.RtnCode,
            refundResult.RtnMsg,
            refundResult.RawResponse,
            cancellationToken);

        if (!refundResult.Succeeded)
        {
            // 業務失敗（綠界拒絕退款）：已留痕待客服查證，不 throw——
            // throw 會讓訊息無限重送，對一個綠界已經明確拒絕的退款沒有意義。
            return;
        }

        if (!payment.Refund(@event.Amount))
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

    private async Task WriteAuditAsync(
        RefundRequested @event,
        PaymentEntity payment,
        bool succeeded,
        string? rtnCode,
        string? rtnMsg,
        string? rawResponse,
        CancellationToken cancellationToken)
    {
        if (auditWriter is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(
            new
            {
                succeeded,
                rtnCode,
                rtnMsg,
                rawResponse,
                amountMinor = @event.Amount.AmountMinor,
                currency = @event.Amount.Currency.ToString(),
                merchantTradeNo = payment.MerchantTradeNo,
                providerTransactionId = payment.ProviderTransactionId,
            },
            GreyGrayJson.Options);

        await auditWriter.WriteAsync(
            AuditCategory.ExternalIntegration,
            succeeded ? "payment.ecpay.refund.succeeded" : "payment.ecpay.refund.failed",
            "Payment",
            payment.Id.ToString(),
            actorId: null,
            subjectId: null,
            reason: @event.Reason,
            payloadJson: payload,
            cancellationToken);
    }
}
