using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Procurement.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────

public readonly record struct PurchaseItemId(Guid Value)
{
    public static PurchaseItemId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

/// <summary>現場詢問客人的一次紀錄。糾紛時這份軌跡就是證據。</summary>
public readonly record struct InquiryId(Guid Value)
{
    public static InquiryId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

// ── 列舉 ─────────────────────────────────────────────────────────────────

public enum PurchaseItemStatus
{
    /// <summary>待採購。截團後由 CampaignClosed 產生。</summary>
    Pending = 0,

    /// <summary>順利買到。</summary>
    Purchased = 1,

    /// <summary>現場漲價，已通知客人、等回覆或等逾時放行。<b>這不是阻塞狀態</b>。</summary>
    PriceChangedPendingConfirmation = 2,

    /// <summary>缺貨。該 OrderLine 取消並退款，其餘 line 續行。</summary>
    Unavailable = 3,
}

public enum InquiryOutcome
{
    /// <summary>客人明確回覆「要」。</summary>
    ConfirmedByCustomer = 1,

    /// <summary>客人明確回覆「不要」。</summary>
    DeclinedByCustomer = 2,

    /// <summary>
    /// <b>逾時視為照買</b>。這是整套系統最「代購」的一個決定——
    /// 它把「問客人」從阻塞式同步等待降級成非阻塞的通知加軌跡記錄，
    /// 你人在店裡不用站著等。差額由你吸收，因為團價已定死台幣。
    /// </summary>
    AutoApprovedOnTimeout = 3,
}

// ── DTO ──────────────────────────────────────────────────────────────────

/// <summary>
/// 採購清單的一項。<b>系統不下單給任何人</b>，
/// 它記錄的是你人站在店裡做了什麼決定。
/// </summary>
public sealed record PurchaseItem(
    PurchaseItemId Id,
    CampaignId CampaignId,
    SkuId SkuId,
    OrderLineId OrderLineId,
    int QuantityRequested,
    int QuantityPurchased,
    Money? TargetPrice,
    MoneyPair? ActualPaid,
    PurchaseItemStatus Status,
    DateTimeOffset? DecidedAt);

/// <summary>
/// 現場詢價的軌跡。要記的只有三件事：問了、幾點問的、客人有沒有回。
/// </summary>
public sealed record Inquiry(
    InquiryId Id,
    PurchaseItemId PurchaseItemId,
    Money OriginalPrice,
    Money NewPrice,
    DateTimeOffset AskedAt,
    DateTimeOffset TimeoutAt,
    DateTimeOffset? RepliedAt,
    InquiryOutcome? Outcome,
    string? ReplyText);

// ── 同步契約 ─────────────────────────────────────────────────────────────

public interface IProcurementQuery
{
    Task<Result<IReadOnlyList<PurchaseItem>>> GetCampaignListAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken);

    Task<Result<Inquiry>> GetInquiryAsync(InquiryId id, CancellationToken cancellationToken);
}

/// <summary>
/// 客人對漲價詢問的回覆入口。
/// <b>LINE 的 postback 直接打到這裡</b>（經 Storefront BFF），
/// 不經由 Notification 回傳事件——支撐模組不該被任何人依賴（ADR-012）。
/// 冪等：同一個 inquiry 重複回覆，以第一次為準。
/// </summary>
public interface IInquiryReplyReceiver
{
    Task<Result> ReplyAsync(
        InquiryId inquiryId,
        bool accepted,
        string? replyText,
        CancellationToken cancellationToken);
}

// ── 對外事件 ─────────────────────────────────────────────────────────────

/// <summary>順利買到。Inventory 訂閱後開批號 ＋ 記成本。</summary>
public sealed record ItemPurchased(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    PurchaseItemId PurchaseItemId,
    CampaignId CampaignId,
    SkuId SkuId,
    OrderLineId OrderLineId,
    int Quantity,
    MoneyPair ActualPaid)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "procurement.ItemPurchased.v1";
}

/// <summary>
/// 帶回入庫。Inventory 訂閱後開新批號；Ledger 訂閱後開 DR 存貨 / CR 現金。
/// </summary>
public sealed record GoodsReceived(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    CampaignId CampaignId,
    SkuId SkuId,
    int Quantity,
    Money UnitCost,
    LotSource Source)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "procurement.GoodsReceived.v1";
}

/// <summary>缺貨。Ordering 訂閱後取消該 OrderLine 並觸發退款。</summary>
public sealed record ItemUnavailable(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    PurchaseItemId PurchaseItemId,
    CampaignId CampaignId,
    OrderLineId OrderLineId,
    string Reason)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "procurement.ItemUnavailable.v1";
}

/// <summary>
/// 現場漲價。Notification 訂閱後發 LINE 問客人。
/// <b>發完就放行，不等回覆</b>——Saga 不需要 AwaitingCustomerResponse 這種會卡住整團的狀態。
/// </summary>
public sealed record ItemPriceChanged(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    PurchaseItemId PurchaseItemId,
    InquiryId InquiryId,
    CampaignId CampaignId,
    OrderLineId OrderLineId,
    SkuId SkuId,
    Money OriginalPrice,
    Money NewPrice,
    DateTimeOffset TimeoutAt)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "procurement.ItemPriceChanged.v1";
}

/// <summary>詢價結案（客人回了，或逾時自動放行）。Audit 訂閱保存軌跡。</summary>
public sealed record InquiryResolved(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    InquiryId InquiryId,
    PurchaseItemId PurchaseItemId,
    InquiryOutcome Outcome)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "procurement.InquiryResolved.v1";
}
