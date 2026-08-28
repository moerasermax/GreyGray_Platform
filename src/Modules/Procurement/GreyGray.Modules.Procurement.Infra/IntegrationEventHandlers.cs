using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Platform.Abstractions.Messaging;

namespace GreyGray.Modules.Procurement.Infra;

internal sealed class CampaignClosedHandler(IProcurementApplication procurement)
    : IIntegrationEventHandler<CampaignClosed>
{
    public async Task HandleAsync(
        CampaignClosed @event,
        CancellationToken cancellationToken)
    {
        var result = await procurement.BuildCampaignListAsync(@event, cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"CampaignClosed 無法建立採購清單：{result.Error.Code} {result.Error.Message}");
        }
    }
}
