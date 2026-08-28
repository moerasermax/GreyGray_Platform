using System.Globalization;
using System.Text;
using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ledger.Contracts;
using GreyGray.Modules.Ledger.Core;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Ledger.Infra;

internal sealed class LedgerQuery(
    LedgerDbContext dbContext,
    ICorrelationContext correlationContext,
    IClock clock) : ILedgerQuery, IStoredValueQuery
{
    public async Task<Result<JournalEntryView>> GetEntryAsync(
        EntryId id,
        CancellationToken cancellationToken)
    {
        var entry = await BaseEntries()
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        return entry is null
            ? Result<JournalEntryView>.Failure("ledger.entry-not-found", "找不到分錄。")
            : entry.ToView();
    }

    public async Task<Result<IReadOnlyList<JournalEntryView>>> GetBySourceAsync(
        string sourceModule,
        string sourceRef,
        CancellationToken cancellationToken)
    {
        var entries = await BaseEntries()
            .Where(entry => entry.SourceModule == sourceModule && entry.SourceRef == sourceRef)
            .OrderByDescending(entry => entry.PostedAt)
            .ToArrayAsync(cancellationToken);
        return entries.Select(entry => entry.ToView()).ToArray();
    }

    public async Task<Result<CampaignMargin>> GetCampaignMarginAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken)
    {
        var lines = await dbContext.Lines.AsNoTracking()
            .Where(line => line.TenantId == correlationContext.TenantId
                && line.CampaignId == campaignId)
            .Select(line => new BalanceLine(line.AccountCode, line.Direction, line.AmountMinor))
            .ToArrayAsync(cancellationToken);
        var currency = Currency.TWD;
        return new CampaignMargin(
            campaignId,
            new Money(NetCredit(lines, AccountCodes.SalesRevenue), currency),
            new Money(NetDebit(lines, AccountCodes.CostOfGoodsSold), currency),
            new Money(NetCredit(lines, AccountCodes.ShippingRevenue), currency),
            new Money(NetDebit(lines, AccountCodes.ShippingCost), currency),
            new Money(NetDebit(lines, AccountCodes.TripCost), currency));
    }

    public async Task<Result<LiabilityVsCash>> GetLiabilityVsCashAsync(
        CancellationToken cancellationToken)
    {
        var codes = AccountCodes.CustomerLiabilityCodes.Append(AccountCodes.Cash).ToArray();
        var lines = await dbContext.Lines.AsNoTracking()
            .Where(line => line.TenantId == correlationContext.TenantId
                && codes.Contains(line.AccountCode))
            .Select(line => new BalanceLine(line.AccountCode, line.Direction, line.AmountMinor))
            .ToArrayAsync(cancellationToken);
        var liability = AccountCodes.CustomerLiabilityCodes.Sum(code => NetCredit(lines, code));
        var cash = NetDebit(lines, AccountCodes.Cash);
        return new LiabilityVsCash(
            new Money(liability, Currency.TWD),
            new Money(cash, Currency.TWD),
            clock.UtcNow);
    }

    public async Task<Result<Money>> GetBalanceAsync(
        CustomerId customerId,
        CancellationToken cancellationToken)
    {
        var lines = await dbContext.Lines.AsNoTracking()
            .Where(line => line.TenantId == correlationContext.TenantId
                && line.CustomerId == customerId
                && line.AccountCode == AccountCodes.CustomerStoredValue)
            .Select(line => new BalanceLine(line.AccountCode, line.Direction, line.AmountMinor))
            .ToArrayAsync(cancellationToken);
        return new Money(NetCredit(lines, AccountCodes.CustomerStoredValue), Currency.TWD);
    }

    public async Task<Result<JournalPage>> SearchAsync(
        JournalSearch search,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(search.Limit, 1, 100);
        var query = BaseEntries();
        if (!string.IsNullOrWhiteSpace(search.SourceModule))
        {
            query = query.Where(entry => entry.SourceModule == search.SourceModule);
        }

        if (!string.IsNullOrWhiteSpace(search.SourceRef))
        {
            query = query.Where(entry => entry.SourceRef == search.SourceRef);
        }

        if (search.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8));
            query = query.Where(entry => entry.OccurredAt >= start);
        }

        if (search.To is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8));
            query = query.Where(entry => entry.OccurredAt < end);
        }

        var hasCursor = TryDecodeCursor(search.Cursor, out var cursorPostedAt, out var cursorId);
        if (!string.IsNullOrWhiteSpace(search.Cursor) && !hasCursor)
        {
            return Result<JournalPage>.Failure(
                "ledger.invalid-cursor",
                "分頁游標格式不正確。");
        }

        if (hasCursor)
        {
            var cursorEntryId = new EntryId(cursorId);
            query = query.Where(entry => entry.PostedAt < cursorPostedAt
                || (entry.PostedAt == cursorPostedAt && entry.Id < cursorEntryId));
        }

        var entries = await query
            .OrderByDescending(entry => entry.PostedAt)
            .ThenByDescending(entry => entry.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = entries.Length > limit;
        var page = entries.Take(limit).ToArray();
        var next = hasMore && page.Length > 0
            ? EncodeCursor(page[^1].PostedAt, page[^1].Id.Value)
            : null;
        return new JournalPage(page.Select(entry => entry.ToView()).ToArray(), next);
    }

    private IQueryable<JournalEntry> BaseEntries() =>
        dbContext.Entries.AsNoTracking()
            .Include(entry => entry.Lines)
            .Where(entry => entry.TenantId == correlationContext.TenantId);

    private static long NetDebit(IEnumerable<BalanceLine> lines, string code) =>
        Sum(lines, code, Direction.Debit) - Sum(lines, code, Direction.Credit);

    private static long NetCredit(IEnumerable<BalanceLine> lines, string code) =>
        Sum(lines, code, Direction.Credit) - Sum(lines, code, Direction.Debit);

    private static long Sum(IEnumerable<BalanceLine> lines, string code, Direction direction) =>
        lines.Where(line => line.AccountCode == code && line.Direction == direction)
            .Sum(line => line.AmountMinor);

    private static string EncodeCursor(DateTimeOffset postedAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{postedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{id:N}"));

    private static bool TryDecodeCursor(
        string? cursor,
        out DateTimeOffset postedAt,
        out Guid id)
    {
        postedAt = default;
        id = default;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 2 ||
                !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !Guid.TryParseExact(parts[1], "N", out id))
            {
                return false;
            }

            postedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record BalanceLine(string AccountCode, Direction Direction, long AmountMinor);
}
