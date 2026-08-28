using System.Globalization;
using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Campaign.Core;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Campaign.Infra;

internal sealed class CampaignRepository(CampaignDbContext dbContext) : ICampaignRepository
{
    public Task<CampaignAggregate?> GetAsync(
        CampaignId id,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        dbContext.Campaigns
            .Include(campaign => campaign.Offers)
            .SingleOrDefaultAsync(
                campaign => campaign.Id == id && campaign.TenantId == tenantId,
                cancellationToken);

    public Task<CampaignOfferEntity?> GetOfferAsync(
        CampaignOfferId id,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        (from offer in dbContext.Offers
         join campaign in dbContext.Campaigns
             on offer.CampaignId equals campaign.Id
         where offer.Id == id && campaign.TenantId == tenantId
         select offer).SingleOrDefaultAsync(cancellationToken);

    public async Task<CampaignPageSlice> ListAsync(
        TenantId tenantId,
        CampaignStatus? status,
        long? beforeUtcTicks,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.TenantId == tenantId);
        if (status is { } selectedStatus)
        {
            query = query.Where(campaign => campaign.Status == selectedStatus);
        }

        if (beforeUtcTicks is { } ticks)
        {
            var before = new DateTimeOffset(ticks, TimeSpan.Zero);
            query = query.Where(campaign => campaign.CreatedAt < before);
        }

        var rows = await query
            .OrderByDescending(campaign => campaign.CreatedAt)
            .ThenByDescending(campaign => campaign.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var items = rows.Take(limit).ToArray();
        var nextCursor = rows.Count > limit
            ? items[^1].CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)
            : null;
        return new CampaignPageSlice(items, nextCursor);
    }

    public void Add(CampaignAggregate campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        dbContext.Campaigns.Add(campaign);
    }
}
