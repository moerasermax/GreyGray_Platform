using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Ledger.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────

public readonly record struct AccountId(Guid Value)
{
    public static AccountId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct EntryId(Guid Value)
{
    public static EntryId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");

    public static bool operator <(EntryId left, EntryId right) => left.Value.CompareTo(right.Value) < 0;

    public static bool operator >(EntryId left, EntryId right) => left.Value.CompareTo(right.Value) > 0;

    public static bool operator <=(EntryId left, EntryId right) => left.Value.CompareTo(right.Value) <= 0;

    public static bool operator >=(EntryId left, EntryId right) => left.Value.CompareTo(right.Value) >= 0;
}

// ── 列舉 ─────────────────────────────────────────────────────────────────

public enum AccountType
{
    Asset = 1,
    Liability = 2,
    Revenue = 3,
    Expense = 4,
    Equity = 5,
}

public enum Direction
{
    Debit = 1,
    Credit = 2,
}

// ── 科目表 ───────────────────────────────────────────────────────────────

/// <summary>
/// 本專案用到的科目代碼。<b>兩種模式（出國採購團／本地批發現貨）用的是同一組科目</b>——
/// 唯一的差別是存貨怎麼進來，那個差別體現在 Saga 的狀態機上，不體現在帳上。
/// </summary>
public static class AccountCodes
{
    // ── ASSET ────────────────────────────────────────────────────────────

    /// <summary>現金／銀行存款。<b>只有撥款到帳後才進這裡</b>。</summary>
    public const string Cash = "1100";

    /// <summary>綠界在途。客人付款當下錢在綠界手上，不在你的帳戶。</summary>
    public const string InTransitECPay = "1151";

    /// <summary>藍新在途（M3）。</summary>
    public const string InTransitNewebPay = "1152";

    /// <summary>LINE Pay 在途（M3）。</summary>
    public const string InTransitLinePay = "1153";

    /// <summary>存貨（批號別）。</summary>
    public const string Inventory = "1300";

    /// <summary>
    /// 通路應收帳款。<b>M1 不啟用，但位置現在就要留</b>（通路擴充接縫 #3）——
    /// 若一開始把通路代收記成現金，之後要回頭改歷史分錄，那是最不想面對的一種資料修補。
    /// </summary>
    public const string ChannelReceivable = "1200";

    // ── LIABILITY ────────────────────────────────────────────────────────

    /// <summary>
    /// 預收貨款＝已收錢、還沒交貨，到期會變成<b>你的收入</b>。
    /// 不要跟「代收貨款」搞混——後者是代客人保管的錢，只有代理關係才有。
    /// 售價已內含服務費，所以本專案是買賣不是代理，用的是這個。
    /// </summary>
    public const string DeferredGoodsRevenue = "2110";

    public const string DeferredShippingRevenue = "2120";

    /// <summary>客戶儲值金。退款走這裡完全不動金流，零手續費。</summary>
    public const string CustomerStoredValue = "2130";

    // ── REVENUE ──────────────────────────────────────────────────────────

    public const string SalesRevenue = "4100";

    public const string ShippingRevenue = "4200";

    // ── EXPENSE ──────────────────────────────────────────────────────────

    public const string CostOfGoodsSold = "5100";

    /// <summary>你付給物流商的成本。與 <see cref="ShippingRevenue"/> 分開記，月結時運費是賺是賠自己會浮出來。</summary>
    public const string ShippingCost = "5200";

    /// <summary>
    /// 旅程成本（機票・住宿・行李超重・關稅）。
    /// 一趟一團，所以直接掛在 Campaign 上，<b>不需要任何攤分規則</b>。
    /// </summary>
    public const string TripCost = "5300";

    /// <summary>金流手續費。撥款到帳時 gross 與 net 的差額。</summary>
    public const string PaymentProcessingFee = "5400";

    /// <summary>
    /// 每日告警用的負債科目清單：
    /// 這三個合計若超過現金餘額，代表你正在用還沒交貨的錢過日子——
    /// 這是代購生意最典型的崩壞前兆。
    /// </summary>
    public static readonly IReadOnlyList<string> CustomerLiabilityCodes =
    [
        DeferredGoodsRevenue,
        DeferredShippingRevenue,
        CustomerStoredValue,
    ];
}

// ── DTO ──────────────────────────────────────────────────────────────────

public sealed record JournalLineView(
    AccountId AccountId,
    string AccountCode,
    Direction Direction,
    Money Amount);

public sealed record JournalEntryView(
    EntryId Id,
    DateTimeOffset OccurredAt,
    DateTimeOffset PostedAt,
    string SourceModule,
    string SourceRef,
    string Memo,
    IReadOnlyList<JournalLineView> Lines);

/// <summary>
/// 每團真實毛利。
/// ＝ 銷貨收入 − 銷貨成本 ＋ 運費收入 − 運費成本 − 旅程成本。
/// </summary>
public sealed record CampaignMargin(
    CampaignId CampaignId,
    Money SalesRevenue,
    Money CostOfGoodsSold,
    Money ShippingRevenue,
    Money ShippingCost,
    Money TripCost)
{
    public Money GrossMargin => SalesRevenue
        .Subtract(CostOfGoodsSold)
        .Add(ShippingRevenue)
        .Subtract(ShippingCost)
        .Subtract(TripCost);
}

public sealed record LiabilityVsCash(
    Money CustomerLiabilityTotal,
    Money CashTotal,
    DateTimeOffset AsOf)
{
    /// <summary>true 代表你正在用還沒交貨的錢過日子。做成每日告警。</summary>
    public bool IsBreached => CustomerLiabilityTotal > CashTotal;
}

public sealed record JournalSearch(
    string? SourceModule,
    string? SourceRef,
    DateOnly? From,
    DateOnly? To,
    string? Cursor,
    int Limit = 50);

public sealed record JournalPage(
    IReadOnlyList<JournalEntryView> Items,
    string? NextCursor);

// ── 同步契約 ─────────────────────────────────────────────────────────────

public interface ILedgerQuery
{
    Task<Result<JournalEntryView>> GetEntryAsync(EntryId id, CancellationToken cancellationToken);

    /// <summary>用 source_module ＋ source_ref 反查——每個數字都要能追回是哪張訂單造成的。</summary>
    Task<Result<IReadOnlyList<JournalEntryView>>> GetBySourceAsync(
        string sourceModule,
        string sourceRef,
        CancellationToken cancellationToken);

    Task<Result<CampaignMargin>> GetCampaignMarginAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken);

    Task<Result<LiabilityVsCash>> GetLiabilityVsCashAsync(CancellationToken cancellationToken);

    Task<Result<JournalPage>> SearchAsync(
        JournalSearch search,
        CancellationToken cancellationToken);
}

public interface IStoredValueQuery
{
    Task<Result<Money>> GetBalanceAsync(CustomerId customerId, CancellationToken cancellationToken);
}

// ── 對外事件 ─────────────────────────────────────────────────────────────

/// <summary>
/// 分錄已入帳。Reporting 訂閱。
/// <b>Ledger 是唯一寫入帳務事實的模組</b>——它訂閱事件、開分錄，不主動呼叫任何人。
/// </summary>
public sealed record JournalPosted(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    EntryId EntryId,
    string SourceModule,
    string SourceRef,
    IReadOnlyList<JournalLineView> Lines)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "ledger.JournalPosted.v1";

    public override string AggregateType => "JournalEntry";

    public override string AggregateId => EntryId.ToString();
}

/// <summary>負債科目合計超過現金餘額。每日檢查，觸發即告警。</summary>
public sealed record LiabilityExceededCash(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    Money LiabilityTotal,
    Money CashTotal)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "ledger.LiabilityExceededCash.v1";

    public override string AggregateType => "LedgerDailyCheck";

    public override string AggregateId => $"{OccurredAt:yyyy-MM-dd}";
}
