using Daigou.Modules.Campaign.Contracts;
using Daigou.Modules.Catalog.Contracts;
using Daigou.Modules.Identity.Contracts;
using Daigou.Modules.Pricing.Contracts;
using Daigou.Platform.Abstractions.Messaging;
using Daigou.Shared.Kernel;

namespace Daigou.Modules.Checkout.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────

public readonly record struct CartId(Guid Value)
{
    public static CartId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct CartLineId(Guid Value)
{
    public static CartLineId New() => new(Guid.CreateVersion7());
}

// ── 列舉 ─────────────────────────────────────────────────────────────────

/// <summary>
/// 履約模式。這是雙模式唯一的分岔點——
/// <b>存貨從哪來、成本何時確定</b>。帳務科目兩者完全相同。
/// </summary>
public enum FulfillmentMode
{
    /// <summary>現貨：台灣本地批發，賣之前已進貨（庫存驅動）。下單即扣可用量，狀態機短路。</summary>
    Stock = 1,

    /// <summary>預購：出國現場採購，賣之後才去買（訂單驅動）。下單只登記需求，走完整 Saga。</summary>
    Preorder = 2,
}

/// <summary>
/// 混合訂單的出貨策略。<b>客人下單時就要選，不是出貨時才問</b>。
/// </summary>
public enum ShippingPolicy
{
    /// <summary>現貨先出，客人早點收到（會付兩次運費）。</summary>
    ShipSeparately = 1,

    /// <summary>等回國一起出，省一次運費。</summary>
    HoldUntilComplete = 2,
}

// ── DTO ──────────────────────────────────────────────────────────────────

public sealed record CartLine(
    CartLineId Id,
    SkuId SkuId,
    FulfillmentMode Mode,
    CampaignOfferId? CampaignOffer,
    int Quantity,
    Money UnitPrice);

public sealed record CartView(
    CartId Id,
    CustomerId? CustomerId,
    IReadOnlyList<CartLine> Lines,
    DeliveryMethod? DeliveryMethod,
    ShippingPolicy ShippingPolicy,
    PricingSnapshot? Quote)
{
    public Money? GrandTotal { get; init; }
}

// ── 同步契約 ─────────────────────────────────────────────────────────────

public interface ICheckoutQuery
{
    Task<Result<CartView>> GetCartAsync(CartId id, CancellationToken cancellationToken);
}

// ── 對外事件 ─────────────────────────────────────────────────────────────

/// <summary>
/// 結帳完成。Ordering 訂閱後建立訂單。
/// <b>Checkout 不建訂單、不收錢</b>——它只負責購物車、詢價與下單前驗證。
/// </summary>
public sealed record CheckoutCompleted(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    CartId CartId,
    CustomerId CustomerId,
    AddressId? ShippingAddressId,
    DeliveryMethod DeliveryMethod,
    ShippingPolicy ShippingPolicy,
    PricingSnapshotId PricingSnapshotId,
    IReadOnlyList<CheckoutLine> Lines,
    string IdempotencyKey)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "checkout.CheckoutCompleted.v1";
}

public sealed record CheckoutLine(
    SkuId SkuId,
    FulfillmentMode Mode,
    CampaignId? CampaignId,
    CampaignOfferId? CampaignOfferId,
    int Quantity,
    Money UnitPrice);
