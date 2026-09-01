using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Inventory.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────

/// <summary>批號。<b>成本的載體</b>——批號別實際成本，出貨時從指定批號結轉銷貨成本。</summary>
public readonly record struct LotId(Guid Value)
{
    public static LotId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct ReservationId(Guid Value)
{
    public static ReservationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

// ── 列舉 ─────────────────────────────────────────────────────────────────

public enum LotSource
{
    /// <summary>台灣本地批發進貨（STOCK 模式，賣之前就進來了）。</summary>
    LocalWholesale = 1,

    /// <summary>出國現場採購帶回（PREORDER 模式，賣之後才去買）。</summary>
    OverseasPurchase = 2,

    /// <summary>客人拒收退回轉現貨。成本沿用原採購成本。</summary>
    CustomerReturn = 3,
}

// ── DTO ──────────────────────────────────────────────────────────────────

public sealed record Lot(
    LotId Id,
    SkuId SkuId,
    LotSource Source,
    Money UnitCost,
    int QuantityOnHand,
    int QuantityReserved,
    CampaignId? FromCampaign,
    string? BatchCode,
    DateTimeOffset ReceivedAt)
{
    /// <summary>
    /// 通路擴充接縫 #4：可用量公式寫成「總量 − 保留」，
    /// <b>並預留再減一項（通路配額）的位置</b>，否則多通路會超賣。
    /// </summary>
    public int QuantityAvailable => QuantityOnHand - QuantityReserved - QuantityChannelAllocated;

    /// <summary>M5 才會有非零值。M1a–M4 一律 0。</summary>
    public int QuantityChannelAllocated { get; init; }
}

public sealed record StockAvailability(SkuId SkuId, int Available);

/// <summary>
/// 批發進貨的輸入（M2）。<b>沒有團</b>——本地批發是 STOCK 模式，賣之前就進來了，
/// 所以 <see cref="Lot.FromCampaign"/> 一律留空。<c>BatchCode</c> 是廠商的批號標示，可以不給。
/// </summary>
public sealed record WholesaleReceipt(
    SkuId SkuId,
    int Quantity,
    Money UnitCost,
    string? BatchCode);

/// <summary>批號列表的查詢條件。游標式分頁，不用 offset（docs/05-API契約.md §5）。</summary>
public sealed record AdminLotListRequest(
    SkuId? SkuId,
    LotId? Cursor,
    int Limit);

public sealed record LotPage(IReadOnlyList<Lot> Items, string? NextCursor);

// ── 同步契約 ─────────────────────────────────────────────────────────────

/// <summary>
/// M2 批發進貨。<b>模組自己有一層冪等</b>：BFF 那一層的冪等鍵在收尾階段出錯時會被
/// abandon，店員用同一把鍵重送就會建出第二個批號——幽靈庫存加一筆多出來的存貨分錄。
/// 所以比照 <c>ShipmentAggregate</c> 與 <c>Cart</c>，把呼叫端的鍵存進聚合，
/// 以它為準判斷「這是重播還是新的一批貨」。
/// </summary>
public interface IInventoryReceiving
{
    Task<Result<Lot>> ReceiveWholesaleAsync(
        WholesaleReceipt receipt,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

/// <summary>
/// 批號列表（後台）。<b>刻意不掛在 <see cref="IInventoryQuery"/> 上</b>——
/// 那個介面是 Checkout 與前台依賴的「可用量」契約，批號列表沒有那些消費者，
/// 把兩者混在一起等於逼每個只需要可用量的呼叫端都認識批號分頁。
/// </summary>
public interface IInventoryLotQuery
{
    Task<Result<LotPage>> ListLotsAsync(
        AdminLotListRequest request,
        CancellationToken cancellationToken);
}

public interface IInventoryQuery
{
    Task<Result<Lot>> GetLotAsync(LotId id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<StockAvailability>>> GetAvailabilityAsync(
        IReadOnlyCollection<SkuId> skuIds,
        CancellationToken cancellationToken);
}

public interface IStockReservation
{
    /// <summary>
    /// 下單時保留庫存（僅 STOCK line）。冪等：同一個 <paramref name="reservationKey"/> 重放回同一個結果。
    /// </summary>
    Task<Result<ReservationId>> ReserveAsync(
        string reservationKey,
        IReadOnlyList<(SkuId SkuId, int Quantity)> lines,
        CancellationToken cancellationToken);

    Task<Result> ReleaseAsync(ReservationId id, CancellationToken cancellationToken);
}

// ── 對外事件 ─────────────────────────────────────────────────────────────

public sealed record LotCreated(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    LotId LotId,
    SkuId SkuId,
    LotSource Source,
    Money UnitCost,
    int Quantity,
    CampaignId? FromCampaign)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "inventory.LotCreated.v1";

    public override string AggregateType => "Lot";

    public override string AggregateId => LotId.ToString();
}

public sealed record StockReserved(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    ReservationId ReservationId,
    SkuId SkuId,
    int Quantity)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "inventory.StockReserved.v1";

    public override string AggregateType => "Reservation";

    public override string AggregateId => ReservationId.ToString();
}

public sealed record StockReleased(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    ReservationId ReservationId)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "inventory.StockReleased.v1";

    public override string AggregateType => "Reservation";

    public override string AggregateId => ReservationId.ToString();
}

/// <summary>
/// 出貨時從批號結轉成本。Ledger 訂閱後開 DR 銷貨成本 / CR 存貨。
/// <b>必須以 OrderLine 為單位結轉</b>：現貨 line 出貨時就能結，預購 line 要等現場採購完成、
/// 成本確定後才有得結。整張訂單一起結會卡住現貨的部分。
/// </summary>
public sealed record StockCostAllocated(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    LotId LotId,
    SkuId SkuId,
    int Quantity,
    Money TotalCost,
    string SourceRef)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "inventory.StockCostAllocated.v1";

    public override string AggregateType => "Lot";

    public override string AggregateId => LotId.ToString();
}
