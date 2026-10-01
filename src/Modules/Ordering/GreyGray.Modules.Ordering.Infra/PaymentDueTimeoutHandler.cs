using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Platform.Abstractions.Saga;

namespace GreyGray.Modules.Ordering.Infra;

/// <summary>繳費期限屆滿時嘗試取消；孤兒、舊 timer 與非待付款狀態由服務層安靜略過。</summary>
internal sealed class PaymentDueTimeoutHandler(OrderingApplicationService ordering)
    : ISagaTimeoutHandler
{
    public static string SagaType => OrderingApplicationService.PaymentDueSagaType;

    public Task HandleTimeoutAsync(
        string sagaId,
        string payload,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sagaId, out var orderGuid))
        {
            return Task.CompletedTask;
        }

        return ordering.ResolvePaymentDueTimeoutAsync(new OrderId(orderGuid), cancellationToken);
    }
}
