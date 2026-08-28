using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Ordering.Infra;

/// <summary>
/// Keeps Campaign coupled to its deliberately small order projection instead of
/// leaking Ordering's aggregate or status machine across the module boundary.
/// </summary>
internal sealed class CampaignOrderQueryAdapter(IOrderQuery orders) : ICampaignOrderQuery
{
    public async Task<Result<CampaignOrderSnapshot>> GetAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken)
    {
        var result = await orders.GetByCampaignAsync(campaignId, cancellationToken);
        if (result.IsFailure)
        {
            return Result<CampaignOrderSnapshot>.Failure(result.Error);
        }

        var campaignOrders = result.Value;
        var quantities = campaignOrders
            .SelectMany(order => order.Lines)
            .Where(line => line.CampaignId == campaignId)
            .GroupBy(line => line.SkuId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(line => line.Quantity));

        // A cancelled order is terminal and no longer blocks settlement, but its
        // historical quantity remains visible so a once-ordered offer cannot be removed.
        var allOrdersShipped = campaignOrders.All(order => order.Status is
            OrderStatus.Shipped or OrderStatus.Completed or OrderStatus.Cancelled);

        return new CampaignOrderSnapshot(
            campaignOrders.Count,
            quantities,
            allOrdersShipped);
    }
}
