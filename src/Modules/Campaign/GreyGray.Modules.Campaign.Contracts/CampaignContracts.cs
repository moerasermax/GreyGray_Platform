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

    /// <summary>
    /// 現場漲價詢問的逾時（ADR-027，每團可設）。
    /// <c>null</c> 表示這個團沒有指定，呼叫端要自己套用技術預設值（現在是 2 小時）。
    /// </summary>
    public TimeSpan? PriceInquiryTimeout { get; init; }
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

/// <summary>前台開團列表項目；是否仍收單由模組依狀態與時鐘計算。</summary>
public sealed record StorefrontCampaignListItem(
    CampaignId Id,
    string Title,
    string Destination,
    DateOnly DepartAt,
    DateOnly ReturnAt,
    DateTimeOffset ClosesAt,
    CampaignStatus Status,
    bool IsAcceptingOrders,
    string? CoverImageUrl);

/// <summary>前台開團商品，包含凍結售價與顯示所需的商品快照。</summary>
public sealed record StorefrontCampaignOffer(
    CampaignOfferId Id,
    SkuId SkuId,
    ProductId ProductId,
    string Name,
    string? VariantName,
    string? ImageUrl,
    Money SellingPrice,
    string? UnitPriceLabel,
    bool IsActive);

/// <summary>前台開團詳情。</summary>
public sealed record StorefrontCampaignDetail(
    StorefrontCampaignListItem Campaign,
    string? Description,
    IReadOnlyList<StorefrontCampaignOffer> Offers);

/// <summary>
/// 一個 SKU 在「目前仍在收單」的團裡的 offer。
/// </summary>
/// <remarks>
/// <b>預購 SKU 的售價只能從這裡來。</b><c>SkuSnapshot.ListPrice</c> 是現貨標價，
/// 不可以拿來冒充開團凍結價——售價在開團時定死，現場買貴買便宜都不影響已成立訂單。
/// </remarks>
public sealed record StorefrontCampaignSkuOffer(
    CampaignOfferId OfferId,
    CampaignId CampaignId,
    Money SellingPrice);

/// <summary>
/// 一個預購商品目前掛在哪一個仍在收單的團上，以及它每個 SKU 的開團定價。
/// </summary>
/// <param name="PriceFrom">
/// 這個商品掛在團上的所有 SKU 裡最低的那個售價，對應契約的 <c>ProductListItem.priceFrom</c>。
/// </param>
/// <param name="Offers">
/// SKU → 該 SKU 的 offer。<b>只含真的掛在收單中的團上的 SKU</b>；
/// 同一個商品可能只有部分規格進了團，沒進團的規格不會出現在這裡。
/// </param>
public sealed record StorefrontProductCampaign(
    ProductId ProductId,
    StorefrontCampaignListItem Campaign,
    Money PriceFrom,
    IReadOnlyDictionary<SkuId, StorefrontCampaignSkuOffer> Offers);

/// <summary>後台建立或修改草稿的輸入。</summary>
/// <param name="PriceInquiryTimeoutMinutes">
/// 現場漲價詢問的逾時分鐘數（ADR-027，每團可設）。
/// 沒填（<c>null</c>）就用技術預設值 2 小時——契約與 UI 都要把這件事講清楚。
/// </param>
public sealed record CampaignDraftInput(
    string Title,
    string Destination,
    DateOnly DepartAt,
    DateOnly ReturnAt,
    DateTimeOffset ClosesAt,
    int? PriceInquiryTimeoutMinutes,
    string? Description,
    string? CoverImageUrl);

/// <summary>後台開團清單項目。</summary>
public sealed record AdminCampaignView(
    CampaignId Id,
    string Title,
    string Destination,
    DateOnly DepartAt,
    DateOnly ReturnAt,
    DateTimeOffset ClosesAt,
    int? PriceInquiryTimeoutMinutes,
    CampaignStatus Status,
    int OrderCount,
    Money? TripCostTotal,
    string? Description,
    string? CoverImageUrl);

/// <summary>後台開團商品。</summary>
public sealed record AdminCampaignOfferView(
    CampaignOfferId Id,
    SkuId SkuId,
    string Name,
    string? VariantName,
    Money SellingPrice,
    Money? TargetPurchasePrice,
    bool IsActive,
    int OrderedQuantity);

/// <summary>後台開團詳情。</summary>
public sealed record AdminCampaignDetail(
    AdminCampaignView Campaign,
    IReadOnlyList<AdminCampaignOfferView> Offers);

/// <summary>加入開團商品的輸入。</summary>
public sealed record CampaignOfferInput(
    SkuId SkuId,
    Money SellingPrice,
    Money? TargetPurchasePrice);

/// <summary>登錄旅程成本的內部 command；TripCostId 是重送去重鍵。</summary>
public sealed record TripCostInput(
    TripCostId Id,
    TripCostKind Kind,
    Money Amount,
    string? Memo);

/// <summary>以 opaque cursor 讀取開團清單。</summary>
public sealed record CampaignPageRequest(
    CampaignStatus? Status = null,
    string? Cursor = null,
    int Limit = 20);

/// <summary>開團清單分頁。</summary>
public sealed record CampaignPage<TItem>(
    IReadOnlyList<TItem> Items,
    string? NextCursor);

// ── 同步契約 ─────────────────────────────────────────────────────────────

public interface ICampaignQuery
{
    Task<Result<CampaignSummary>> GetAsync(CampaignId id, CancellationToken cancellationToken);

    Task<Result<CampaignOffer>> GetOfferAsync(CampaignOfferId id, CancellationToken cancellationToken);

    /// <summary>下單前驗證：這個團還在收單嗎、這個商品還開著嗎。</summary>
    Task<Result<bool>> IsAcceptingOrdersAsync(CampaignId id, CancellationToken cancellationToken);
}

/// <summary>
/// Campaign Core 判斷商品是否已有訂單及能否結團所需的最小訂單投影。
/// 由 Ordering 的組合層實作，避免 Campaign 依賴 Ordering 的內部狀態機。
/// </summary>
public interface ICampaignOrderQuery
{
    Task<Result<CampaignOrderSnapshot>> GetAsync(
        CampaignId campaignId,
        CancellationToken cancellationToken);
}

/// <summary>一個開團的訂單統計，不暴露 Ordering 聚合。</summary>
public sealed record CampaignOrderSnapshot(
    int OrderCount,
    IReadOnlyDictionary<SkuId, int> OrderedQuantityBySku,
    bool AllOrdersShipped);

/// <summary>Storefront BFF 可呼叫的開團 input port。</summary>
public interface ICampaignStorefront
{
    Task<Result<CampaignPage<StorefrontCampaignListItem>>> ListAsync(
        CampaignPageRequest request,
        CancellationToken cancellationToken);

    Task<Result<StorefrontCampaignDetail>> GetDetailAsync(
        CampaignId id,
        CancellationToken cancellationToken);

    /// <summary>
    /// 用商品批次反查「目前仍在收單」的開團定價。前台商品列表與商品詳情靠它把
    /// <c>campaignId</c>／<c>campaign</c>／<c>price</c>／<c>campaignOfferId</c> 填出來。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>批次是必要的</b>：商品列表一次要問一整頁的商品，逐一問會變 N+1。
    /// 沒有掛在收單中的團上的商品<b>不會出現在回傳的字典裡</b>（不是給一個空值）。
    /// </para>
    /// <para>
    /// <b>只看仍在收單的團</b>——<c>Status = Open</c> 且尚未到 <c>ClosesAt</c>，
    /// 判斷一律由模組依自己的時鐘做，呼叫端不要自己拿 <c>ClosesAt</c> 跟現在時間比。
    /// </para>
    /// <para>
    /// <b>同一個 SKU 掛在多個收單中的團上時取 <c>ClosesAt</c> 最早的那一個。</b>
    /// <c>campaign_offer</c> 沒有「一個 SKU 只能屬於一個團」的唯一鍵，所以這件事擋不住，
    /// 只能定規則：最快截止的優先，與前台「LastCall」的語意一致。<c>ClosesAt</c> 相同時
    /// 再用 <c>CampaignId</c> 定序，讓同一批資料永遠得到同一個答案。
    /// 商品層的團同理——取這個商品命中的團裡 <c>ClosesAt</c> 最早的那一個。
    /// </para>
    /// </remarks>
    Task<Result<IReadOnlyDictionary<ProductId, StorefrontProductCampaign>>> FindOpenCampaignPricingAsync(
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken);
}

/// <summary>Admin BFF 可呼叫的開團 input port；所有狀態規則仍由 Campaign Core 判斷。</summary>
public interface ICampaignAdministration
{
    Task<Result<CampaignPage<AdminCampaignView>>> ListAsync(
        CampaignPageRequest request,
        CancellationToken cancellationToken);

    Task<Result<AdminCampaignDetail>> GetDetailAsync(
        CampaignId id,
        CancellationToken cancellationToken);

    Task<Result<AdminCampaignView>> CreateDraftAsync(
        CampaignDraftInput input,
        CancellationToken cancellationToken);

    Task<Result<AdminCampaignView>> UpdateDraftAsync(
        CampaignId id,
        CampaignDraftInput input,
        CancellationToken cancellationToken);

    Task<Result<AdminCampaignView>> PublishAsync(
        CampaignId id,
        CancellationToken cancellationToken);

    Task<Result<AdminCampaignView>> CloseAsync(
        CampaignId id,
        CancellationToken cancellationToken);

    Task<Result<AdminCampaignView>> CancelAsync(
        CampaignId id,
        string reason,
        CancellationToken cancellationToken);

    Task<Result<AdminCampaignView>> SettleAsync(
        CampaignId id,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<AdminCampaignOfferView>>> GetOffersAsync(
        CampaignId id,
        CancellationToken cancellationToken);

    Task<Result<AdminCampaignOfferView>> AddOfferAsync(
        CampaignId id,
        CampaignOfferInput input,
        CancellationToken cancellationToken);

    Task<Result> RemoveOfferAsync(
        CampaignId campaignId,
        CampaignOfferId offerId,
        CancellationToken cancellationToken);
}

/// <summary>旅程成本 command 的最小 input port。</summary>
public interface ICampaignTripCostAdministration
{
    /// <summary>登錄一筆直接屬於本團的旅程成本；不做跨團攤分。</summary>
    Task<Result<AdminCampaignView>> RecordTripCostAsync(
        CampaignId campaignId,
        TripCostInput input,
        CancellationToken cancellationToken);
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
