using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Platform.Abstractions.Saga;

namespace GreyGray.Modules.Ordering.Infra;

/// <summary>
/// 鑑賞期屆滿 → 轉 Completed（ADR-025）。Worker 的 Saga timer dispatcher 到期時呼叫，
/// 實際轉移邏輯與孤兒判斷都在 <see cref="OrderingApplicationService"/>。
/// </summary>
internal sealed class AppraisalPeriodTimeoutHandler(OrderingApplicationService ordering)
    : ISagaTimeoutHandler
{
    public static string SagaType => OrderingApplicationService.AppraisalSagaType;

    public Task HandleTimeoutAsync(
        string sagaId,
        string payload,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sagaId, out var orderGuid))
        {
            return Task.CompletedTask;
        }

        return ordering.ResolveAppraisalTimeoutAsync(new OrderId(orderGuid), cancellationToken);
    }
}
