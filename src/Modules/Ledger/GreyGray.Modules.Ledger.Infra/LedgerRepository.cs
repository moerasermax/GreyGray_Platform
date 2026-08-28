using GreyGray.Modules.Ledger.Core;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Ledger.Infra;

internal sealed class LedgerRepository(LedgerDbContext dbContext) : ILedgerRepository
{
    public async Task<LedgerAccount> GetAccountAsync(
        TenantId tenantId,
        string code,
        CancellationToken cancellationToken) =>
        await dbContext.Accounts.SingleOrDefaultAsync(
            account => account.TenantId == tenantId && account.Code == code,
            cancellationToken) ?? throw new InvalidOperationException(
            $"Ledger 科目 {code} 尚未在 tenant {tenantId} 建立。");

    public Task<bool> SourceExistsAsync(
        TenantId tenantId,
        string sourceModule,
        string sourceRef,
        CancellationToken cancellationToken) =>
        dbContext.Entries.AnyAsync(
            entry => entry.TenantId == tenantId &&
                     entry.SourceModule == sourceModule &&
                     entry.SourceRef == sourceRef,
            cancellationToken);

    public void Add(JournalEntry entry) => dbContext.Entries.Add(entry);
}
