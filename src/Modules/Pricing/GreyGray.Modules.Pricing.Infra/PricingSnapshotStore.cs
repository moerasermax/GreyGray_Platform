using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Modules.Pricing.Core;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Pricing.Infra;

internal sealed class PricingSnapshotStore(
    PricingDbContext dbContext,
    ICorrelationContext correlationContext) : IPricingSnapshotStore
{
    public async Task<PricingSnapshot?> GetAsync(
        PricingSnapshotId id,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.PricingSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(
                snapshot => snapshot.Id == id
                    && snapshot.TenantId == correlationContext.TenantId,
                cancellationToken);
        return entity?.ToContract();
    }

    public void Add(PricingSnapshot snapshot) =>
        dbContext.PricingSnapshots.Add(
            PricingSnapshotEntity.From(correlationContext.TenantId, snapshot));
}
