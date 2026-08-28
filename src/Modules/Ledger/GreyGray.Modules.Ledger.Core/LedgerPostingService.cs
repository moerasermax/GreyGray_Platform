using GreyGray.Modules.Ledger.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Ledger.Core;

internal sealed class LedgerPostingService(
    ILedgerRepository ledger,
    IEventPublisher eventPublisher,
    IClock clock)
{
    public async Task<JournalEntry?> PostAsync(
        TenantId tenantId,
        DateTimeOffset occurredAt,
        string sourceModule,
        string sourceRef,
        string memo,
        IReadOnlyList<PostingLine> lines,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceModule);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);
        ArgumentNullException.ThrowIfNull(lines);

        if (await ledger.SourceExistsAsync(
                tenantId,
                sourceModule,
                sourceRef,
                cancellationToken))
        {
            return null;
        }

        var postedAt = clock.UtcNow;
        var entry = new JournalEntry(
            EntryId.New(),
            tenantId,
            occurredAt,
            postedAt,
            sourceModule,
            sourceRef,
            memo);

        foreach (var line in lines.Where(line => !line.Amount.IsZero))
        {
            var account = await ledger.GetAccountAsync(
                tenantId,
                line.AccountCode,
                cancellationToken);
            entry.AddLine(
                account,
                line.Direction,
                line.Amount,
                line.CampaignId,
                line.CustomerId);
        }

        entry.AssertBalanced();
        ledger.Add(entry);
        await eventPublisher.PublishAsync(
            new JournalPosted(
                Guid.CreateVersion7(),
                postedAt,
                tenantId,
                entry.Id,
                sourceModule,
                sourceRef,
                entry.Lines.Select(line => line.ToView()).ToArray()),
            cancellationToken);
        return entry;
    }
}
