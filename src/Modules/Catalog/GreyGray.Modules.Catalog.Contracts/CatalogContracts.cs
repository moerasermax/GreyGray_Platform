using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Catalog.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────
// 通路擴充接縫 #2：SKU 一律用內部產生的穩定 ID。
// 不要拿商品編號、條碼或名稱當主鍵；內部 ID 一經產生不可變。

public readonly record struct ProductId(Guid Value)
{
    public static ProductId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct SkuId(Guid Value)
{
    public static SkuId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct CategoryId(Guid Value)
{
    public static CategoryId New() => new(Guid.CreateVersion7());
}

// ── DTO ──────────────────────────────────────────────────────────────────

/// <summary>
/// 給其他模組用的 SKU 快照。
/// <b>WeightGram 與 Size 在 M1a 就要填</b>——M1a 運費是一口價用不到，
/// 但等 M3 啟用材積重計費時資料已經在那裡了，回頭補幾百筆商品尺寸是純粹的浪費。
/// </summary>
public sealed record SkuSnapshot(
    SkuId Id,
    ProductId ProductId,
    string Name,
    string? VariantName,
    int WeightGram,
    Dimensions Size,
    bool IsActive)
{
    /// <summary>單位價格用的顯示字串來源，例如「NT$780／32 顆」的分母。前台規則 #4。</summary>
    public string? UnitOfMeasure { get; init; }

    public int? UnitCount { get; init; }
}

// ── 同步契約 ─────────────────────────────────────────────────────────────

public interface ICatalogQuery
{
    Task<Result<SkuSnapshot>> GetSkuAsync(SkuId id, CancellationToken cancellationToken);

    /// <summary>批次取，給 Checkout 詢價與 Pricing 算材積重用。缺任何一個都算失敗。</summary>
    Task<Result<IReadOnlyList<SkuSnapshot>>> GetSkusAsync(
        IReadOnlyCollection<SkuId> ids,
        CancellationToken cancellationToken);
}

// ── 對外事件 ─────────────────────────────────────────────────────────────

public sealed record SkuPublished(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    SkuId SkuId,
    ProductId ProductId,
    string Name)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "catalog.SkuPublished.v1";
}

public sealed record SkuArchived(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    SkuId SkuId)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "catalog.SkuArchived.v1";
}

/// <summary>
/// 商品資訊變更（名稱、重量、尺寸）。M4 的 Channel 模組會訂閱這個做出站同步。
/// <b>價格不在這裡</b>——售價屬於 Campaign 的開團定價或 Inventory 的現貨標價。
/// </summary>
public sealed record SkuAttributesChanged(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    SkuId SkuId,
    int WeightGram,
    Dimensions Size)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "catalog.SkuAttributesChanged.v1";
}
