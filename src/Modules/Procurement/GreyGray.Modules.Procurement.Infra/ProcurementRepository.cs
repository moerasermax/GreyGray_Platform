using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Modules.Procurement.Core;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Procurement.Infra;

internal sealed class ProcurementRepository(ProcurementDbContext dbContext)
    : IProcurementRepository
{
    public Task<PurchaseItemAggregate?> GetAsync(
        TenantId tenantId,
        PurchaseItemId id,
        CancellationToken cancellationToken) =>
        dbContext.PurchaseItems.SingleOrDefaultAsync(
            item => item.TenantId == tenantId && item.Id == id,
            cancellationToken);

    public async Task<IReadOnlyList<PurchaseItemAggregate>> GetCampaignListAsync(
        TenantId tenantId,
        CampaignId campaignId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseItems
            .Where(item => item.TenantId == tenantId && item.CampaignId == campaignId);
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query
            .OrderBy(item => item.Status)
            .ThenBy(item => item.CreatedAt)
            .ThenBy(item => item.Id.Value)
            .ToArrayAsync(cancellationToken);
    }

    public void Add(PurchaseItemAggregate item) => dbContext.PurchaseItems.Add(item);
}
