using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Catalog.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────
// 通路擴充接縫 #2：SKU 一律用內部產生的穩定 ID。
// 不要拿商品編號、條碼或名稱當主鍵；內部 ID 一經產生不可變。

public readonly record struct ProductId(Guid Value)
{
    public static ProductId New() => new(Guid.CreateVersion7());

    public static bool operator <(ProductId left, ProductId right) => left.Value.CompareTo(right.Value) < 0;
    public static bool operator >(ProductId left, ProductId right) => left.Value.CompareTo(right.Value) > 0;
    public static bool operator <=(ProductId left, ProductId right) => left.Value.CompareTo(right.Value) <= 0;
    public static bool operator >=(ProductId left, ProductId right) => left.Value.CompareTo(right.Value) >= 0;

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

    public override string ToString() => Value.ToString("N");
}

public enum FulfillmentMode
{
    Stock = 0,
    Preorder = 1,
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

    /// <summary>
    /// 現貨公開售價；預購售價仍只可取自 CampaignOffer，不能用這個欄位取代開團凍結價。
    /// </summary>
    public Money? ListPrice { get; init; }
}

public sealed record CategoryInput(string Name, string? ImageUrl, int SortOrder);

public sealed record CategoryView(
    CategoryId Id,
    string Name,
    string? ImageUrl,
    int SortOrder);

/// <summary>符合 frozen AdminSkuInput；M1a 起重量與三邊尺寸皆為必要值。</summary>
public sealed record AdminSkuInput(
    string Name,
    string? VariantName,
    int WeightGram,
    Dimensions Size,
    string? UnitOfMeasure,
    int? UnitCount,
    Money? ListPrice,
    bool IsActive);

public sealed record AdminSkuView(
    SkuId Id,
    ProductId ProductId,
    string Name,
    string? VariantName,
    int WeightGram,
    Dimensions Size,
    string? UnitOfMeasure,
    int? UnitCount,
    Money? ListPrice,
    bool IsActive);

/// <summary>
/// frozen AdminProductInput 不含 SKU；商品可先建為空 SKU 集，首個 SKU 必須另走
/// <see cref="ICatalogAdministration.CreateSkuAsync"/>。
/// </summary>
public sealed record AdminProductInput(
    string Name,
    string? Description,
    string? ShortDescription,
    CategoryId? CategoryId,
    FulfillmentMode Mode,
    IReadOnlyList<string>? Images,
    bool IsActive);

public sealed record AdminProductView(
    ProductId Id,
    string Name,
    string? Description,
    string? ShortDescription,
    CategoryId? CategoryId,
    FulfillmentMode Mode,
    IReadOnlyList<string> Images,
    bool IsActive,
    IReadOnlyList<AdminSkuView> Skus);

public sealed record StorefrontProductListItem(
    ProductId Id,
    string Name,
    string? ShortDescription,
    string? ImageUrl,
    Money? PriceFrom,
    string? UnitPriceLabel,
    FulfillmentMode Mode);

public sealed record StorefrontProductDetail(
    ProductId Id,
    string Name,
    string? Description,
    string? ShortDescription,
    CategoryId? CategoryId,
    IReadOnlyList<string> Images,
    FulfillmentMode Mode,
    IReadOnlyList<AdminSkuView> Skus);

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record ProductSearch(
    string? Query,
    CategoryId? CategoryId,
    FulfillmentMode? Mode,
    bool IncludeArchived,
    string? Cursor,
    int Limit);

// ── 同步契約 ─────────────────────────────────────────────────────────────

public interface ICatalogQuery
{
    Task<Result<SkuSnapshot>> GetSkuAsync(SkuId id, CancellationToken cancellationToken);

    /// <summary>批次取，給 Checkout 詢價與 Pricing 算材積重用。缺任何一個都算失敗。</summary>
    Task<Result<IReadOnlyList<SkuSnapshot>>> GetSkusAsync(
        IReadOnlyCollection<SkuId> ids,
        CancellationToken cancellationToken);
}

public interface IStorefrontCatalogQuery
{
    Task<IReadOnlyList<CategoryView>> ListCategoriesAsync(CancellationToken cancellationToken);

    Task<Result<CursorPage<StorefrontProductListItem>>> ListProductsAsync(
        ProductSearch search,
        CancellationToken cancellationToken);

    Task<Result<StorefrontProductDetail>> GetProductAsync(
        ProductId productId,
        CancellationToken cancellationToken);
}

/// <summary>登入客戶的商品最愛清單（ADR-036）。</summary>
public interface IStorefrontFavorites
{
    Task<Result> AddAsync(
        CustomerId customerId,
        ProductId productId,
        CancellationToken cancellationToken);

    Task<Result> RemoveAsync(
        CustomerId customerId,
        ProductId productId,
        CancellationToken cancellationToken);

    Task<Result<CursorPage<StorefrontProductListItem>>> ListAsync(
        CustomerId customerId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<ProductId>> FindAsync(
        CustomerId customerId,
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken);
}

public interface ICatalogAdministration
{
    Task<IReadOnlyList<CategoryView>> ListCategoriesAsync(CancellationToken cancellationToken);

    Task<Result<CategoryView>> CreateCategoryAsync(
        CategoryInput input,
        CancellationToken cancellationToken);

    Task<Result<CategoryView>> UpdateCategoryAsync(
        CategoryId categoryId,
        CategoryInput input,
        CancellationToken cancellationToken);

    Task<Result<CursorPage<AdminProductView>>> ListProductsAsync(
        ProductSearch search,
        CancellationToken cancellationToken);

    Task<Result<AdminProductView>> GetProductAsync(
        ProductId productId,
        CancellationToken cancellationToken);

    Task<Result<AdminProductView>> CreateProductAsync(
        AdminProductInput input,
        CancellationToken cancellationToken);

    Task<Result<AdminProductView>> UpdateProductAsync(
        ProductId productId,
        AdminProductInput input,
        CancellationToken cancellationToken);

    Task<Result<AdminSkuView>> CreateSkuAsync(
        ProductId productId,
        AdminSkuInput input,
        CancellationToken cancellationToken);

    Task<Result<AdminSkuView>> UpdateSkuAsync(
        SkuId skuId,
        AdminSkuInput input,
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

    public override string AggregateType => "Sku";

    public override string AggregateId => SkuId.ToString();
}

public sealed record SkuArchived(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    SkuId SkuId)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "catalog.SkuArchived.v1";

    public override string AggregateType => "Sku";

    public override string AggregateId => SkuId.ToString();
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

    public override string AggregateType => "Sku";

    public override string AggregateId => SkuId.ToString();
}
