using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Campaign.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────

public readonly record struct CampaignId(Guid Value)
{
    public static CampaignId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct CampaignOfferId(Guid Value)
{
    public static CampaignOfferId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct TripCostId(Guid Value)
{
    public static TripCostId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

// ── 列舉 ─────────────────────────────────────────────────────────────────

public enum CampaignStatus
{
    Draft = 0,
    Open = 1,
    Closed = 2,
    TripInProgress = 3,
    Returned = 4,
    Settled = 5,
    Cancelled = 9,
}

/// <summary>旅程成本科目。M1a 全部記在同一個「旅程成本」科目下，細分排 M6+。</summary>
public enum TripCostKind
{
    Airfare = 1,
    Accommodation = 2,
    ExcessBaggage = 3,
    CustomsDuty = 4,
    LocalTransport = 5,
    Other = 99,
}

// ── DTO ──────────────────────────────────────────────────────────────────

/// <summary>
/// 開團 ＝ 一趟採購旅程。<b>一趟旅程對應一個團（1:1）</b>，
/// 所以旅程成本直接掛在 Campaign 上，不需要任何攤分規則——
/// 月結時「每團真實毛利」這個數字自己就出來了。
/// </summary>
public sealed record CampaignSummary(
    CampaignId Id,
    string Title,
    string Destination,
    DateOnly DepartAt,
    DateOnly ReturnAt,
    DateTimeOffset ClosesAt,
    CampaignStatus Status)
{
    /// <summary>已登錄的旅程成本合計。回國前是 0。</summary>
    public Money? TripCostTotal { get; init; }
}

/// <summary>
/// 開團商品。<b>沒有成團門檻、沒有名額上限</b>——下單只登記需求（訂單驅動）。
/// 售價在開團時定死，現場買貴買便宜都不影響已成立訂單。
/// </summary>
public sealed record CampaignOffer(
    CampaignOfferId Id,
    CampaignId CampaignId,
    SkuId SkuId,
    Money SellingPrice,
    Money? TargetPurchasePrice,
    bool IsActive);

// ── 同步契約 ─────────────────────────────────────────────────────────────

public interface ICampaignQuery
{
    Task<Result<CampaignSummary>> GetAsync(CampaignId id, CancellationToken cancellationToken);

    Task<Result<CampaignOffer>> GetOfferAsync(CampaignOfferId id, CancellationToken cancellationToken);

    /// <summary>下單前驗證：這個團還在收單嗎、這個商品還開著嗎。</summary>
    Task<Result<bool>> IsAcceptingOrdersAsync(CampaignId id, CancellationToken cancellationToken);
}

// ── 對外事件 ─────────────────────────────────────────────────────────────

public sealed record CampaignPublished(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    CampaignId CampaignId,
    string Destination,
    DateTimeOffset ClosesAt)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "campaign.CampaignPublished.v1";

    public override string AggregateType => "Campaign";

    public override string AggregateId => CampaignId.ToString();
}

/// <summary>截團。Procurement 訂閱後產出採購清單。</summary>
public sealed record CampaignClosed(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    CampaignId CampaignId)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "campaign.CampaignClosed.v1";

    public override string AggregateType => "Campaign";

    public override string AggregateId => CampaignId.ToString();
}

/// <summary>
/// 旅程成本登錄。Ledger 訂閱後開分錄 DR 旅程成本 / CR 現金。
/// <b>團被取消時這筆仍然要入帳</b>——機票已經買了就是真實損失，不能因為團沒成就假裝沒發生。
/// </summary>
public sealed record TripCostRecorded(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    CampaignId CampaignId,
    TripCostId TripCostId,
    TripCostKind Kind,
    Money Amount,
    string Memo)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "campaign.TripCostRecorded.v1";

    public override string AggregateType => "Campaign";

    public override string AggregateId => CampaignId.ToString();
}

public sealed record CampaignCancelled(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    CampaignId CampaignId,
    string Reason)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "campaign.CampaignCancelled.v1";

    public override string AggregateType => "Campaign";

    public override string AggregateId => CampaignId.ToString();
}

/// <summary>該團所有訂單皆已出貨，可以結團算毛利了。Reporting 訂閱。</summary>
public sealed record CampaignSettled(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    CampaignId CampaignId)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "campaign.CampaignSettled.v1";

    public override string AggregateType => "Campaign";

    public override string AggregateId => CampaignId.ToString();
}
