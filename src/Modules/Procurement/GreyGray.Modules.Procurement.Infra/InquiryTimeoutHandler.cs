using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Modules.Procurement.Core;
using GreyGray.Platform.Abstractions.Saga;

namespace GreyGray.Modules.Procurement.Infra;

/// <summary>
/// 現場漲價詢問逾時 → 視為照買。Worker 的 Saga timer dispatcher 到期時呼叫，
/// 實際解決邏輯與客人明講回覆共用同一段（<see cref="ProcurementApplicationService"/>）。
/// </summary>
internal sealed class InquiryTimeoutHandler(ProcurementApplicationService procurement)
    : ISagaTimeoutHandler
{
    public static string SagaType => ProcurementApplicationService.PriceInquirySagaType;

    public Task HandleTimeoutAsync(
        string sagaId,
        string payload,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sagaId, out var inquiryGuid))
        {
            return Task.CompletedTask;
        }

        return procurement.ResolveInquiryTimeoutAsync(new InquiryId(inquiryGuid), cancellationToken);
    }
}
