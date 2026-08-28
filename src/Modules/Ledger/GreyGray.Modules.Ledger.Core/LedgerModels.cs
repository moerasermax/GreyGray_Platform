using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ledger.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Ledger.Core;

internal sealed class LedgerAccount
{
    private LedgerAccount()
    {
    }

    public LedgerAccount(AccountId id, TenantId tenantId, string code, string name, AccountType type)
    {
        Id = id;
        TenantId = tenantId;
        Code = code;
        Name = name;
        Type = type;
    }

    public AccountId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public AccountType Type { get; private set; }
}

internal sealed class JournalEntry
{
    private readonly List<JournalLine> _lines = [];

    private JournalEntry()
    {
    }

    public JournalEntry(
        EntryId id,
        TenantId tenantId,
        DateTimeOffset occurredAt,
        DateTimeOffset postedAt,
        string sourceModule,
        string sourceRef,
        string memo)
    {
        Id = id;
        TenantId = tenantId;
        OccurredAt = occurredAt;
        PostedAt = postedAt;
        SourceModule = sourceModule;
        SourceRef = sourceRef;
        Memo = memo;
    }

    public EntryId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset PostedAt { get; private set; }

    public string SourceModule { get; private set; } = string.Empty;

    public string SourceRef { get; private set; } = string.Empty;

    public string Memo { get; private set; } = string.Empty;

    public IReadOnlyCollection<JournalLine> Lines => _lines;

    public void AddLine(
        LedgerAccount account,
        Direction direction,
        Money amount,
        CampaignId? campaignId = null,
        CustomerId? customerId = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.TenantId != TenantId)
        {
            throw new InvalidOperationException("分錄與科目必須屬於同一租戶。");
        }

        if (!Enum.IsDefined(direction))
        {
            throw new InvalidOperationException("分錄方向必須是借方或貸方。");
        }

        if (amount.IsNegative || amount.IsZero)
        {
            throw new InvalidOperationException("分錄金額必須大於零。");
        }

        _lines.Add(new JournalLine(
            Guid.CreateVersion7(),
            TenantId,
            Id,
            account.Id,
            account.Code,
            direction,
            amount.AmountMinor,
            amount.Currency,
            campaignId,
            customerId));
    }

    public void AssertBalanced()
    {
        if (_lines.Count < 2)
        {
            throw new InvalidOperationException("分錄至少需要一借一貸兩條 line。");
        }

        foreach (var group in _lines.GroupBy(line => line.Currency))
        {
            var debits = group.Where(line => line.Direction == Direction.Debit)
                .Sum(line => line.AmountMinor);
            var credits = group.Where(line => line.Direction == Direction.Credit)
                .Sum(line => line.AmountMinor);
            if (debits != credits)
            {
                throw new InvalidOperationException(
                    $"分錄 {Id} 不平衡：{group.Key} DR={debits}, CR={credits}。");
            }
        }

    }

    public JournalEntryView ToView() => new(
        Id,
        OccurredAt,
        PostedAt,
        SourceModule,
        SourceRef,
        Memo,
        _lines.Select(line => line.ToView()).ToArray());
}

internal sealed class JournalLine
{
    private JournalLine()
    {
    }

    public JournalLine(
        Guid id,
        TenantId tenantId,
        EntryId entryId,
        AccountId accountId,
        string accountCode,
        Direction direction,
        long amountMinor,
        Currency currency,
        CampaignId? campaignId,
        CustomerId? customerId)
    {
        Id = id;
        TenantId = tenantId;
        EntryId = entryId;
        AccountId = accountId;
        AccountCode = accountCode;
        Direction = direction;
        AmountMinor = amountMinor;
        Currency = currency;
        CampaignId = campaignId;
        CustomerId = customerId;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public EntryId EntryId { get; private set; }

    public AccountId AccountId { get; private set; }

    public string AccountCode { get; private set; } = string.Empty;

    public Direction Direction { get; private set; }

    public long AmountMinor { get; private set; }

    public Currency Currency { get; private set; }

    public CampaignId? CampaignId { get; private set; }

    public CustomerId? CustomerId { get; private set; }

    public JournalLineView ToView() => new(
        AccountId,
        AccountCode,
        Direction,
        new Money(AmountMinor, Currency));
}

internal sealed record PostingLine(
    string AccountCode,
    Direction Direction,
    Money Amount,
    CampaignId? CampaignId = null,
    CustomerId? CustomerId = null);

internal interface ILedgerRepository
{
    Task<LedgerAccount> GetAccountAsync(
        TenantId tenantId,
        string code,
        CancellationToken cancellationToken);

    Task<bool> SourceExistsAsync(
        TenantId tenantId,
        string sourceModule,
        string sourceRef,
        CancellationToken cancellationToken);

    void Add(JournalEntry entry);
}
