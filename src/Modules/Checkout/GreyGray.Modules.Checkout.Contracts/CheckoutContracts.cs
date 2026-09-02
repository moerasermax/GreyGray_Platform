using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Checkout.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────

public readonly record struct CartId(Guid Value)
{
    public static CartId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct CartLineId(Guid Value)
{
    public static CartLineId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
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
    Money UnitPrice)
{
    public ProductId ProductId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? VariantName { get; init; }

    public string? ImageUrl { get; init; }

    public CampaignId? CampaignId { get; init; }

    public Money LineTotal => UnitPrice.MultiplyByQuantity(Quantity);

    public string? AvailabilityWarning { get; init; }
}

public sealed record CartView(
    CartId Id,
    CustomerId? CustomerId,
    IReadOnlyList<CartLine> Lines,
    DeliveryMethod? DeliveryMethod,
    ShippingPolicy ShippingPolicy,
    PricingSnapshot? Quote)
{
    public Money GoodsTotal { get; init; } = Money.Zero(Currency.TWD);

    public bool HasMixedModes { get; init; }

    public Money? GrandTotal { get; init; }
}

public sealed record CheckoutQuote(
    PricingSnapshot Snapshot,
    Money GoodsTotal,
    Money GrandTotal);

public sealed record AddCartLineRequest(
    CartId CartId,
    CustomerId? CustomerId,
    SkuId SkuId,
    FulfillmentMode Mode,
    CampaignOfferId? CampaignOfferId,
    int Quantity);

public sealed record UpdateCartLineRequest(
    CartId CartId,
    CustomerId? CustomerId,
    CartLineId LineId,
    int Quantity);

public sealed record RemoveCartLineRequest(
    CartId CartId,
    CustomerId? CustomerId,
    CartLineId LineId);

public sealed record QuoteCartRequest(
    CartId CartId,
    CustomerId? CustomerId,
    DeliveryMethod DeliveryMethod);

/// <param name="ShippingPolicy">
/// ADR-030：只有<b>混合購物車</b>（同時有現貨與預購 line）才必須帶值，缺了回
/// <c>checkout.shipping-policy-required</c>；單一模式時這個值會被<b>忽略</b>，
/// 由 Checkout 依 line 組成推導（純現貨 → <see cref="ShippingPolicy.ShipSeparately"/>、
/// 純預購 → <see cref="ShippingPolicy.HoldUntilComplete"/>）。規則的主人是後端，
/// 不下放給任何客戶端。
/// </param>
public sealed record CompleteCheckoutRequest(
    CartId CartId,
    CustomerId CustomerId,
    DeliveryMethod DeliveryMethod,
    ShippingPolicy? ShippingPolicy,
    AddressId? ShippingAddressId,
    string? ConvenienceStoreCode,
    string? BuyerNote,
    string IdempotencyKey);

// ── 同步契約 ─────────────────────────────────────────────────────────────

public interface ICheckoutQuery
{
    Task<Result<CartView>> GetCartAsync(CartId id, CancellationToken cancellationToken);
}

/// <summary>
/// Storefront BFF 的 Checkout input port。BFF 只負責 session、idempotency 與 HTTP mapping；
/// 購物車 ownership、驗證、報價與結帳規則都留在模組內。
/// </summary>
public interface ICheckoutApplication
{
    Task<Result<CartView>> GetCartAsync(
        CartId cartId,
        CustomerId? customerId,
        CancellationToken cancellationToken);

    Task<Result<CartView>> AddLineAsync(
        AddCartLineRequest request,
        CancellationToken cancellationToken);

    Task<Result<CartView>> UpdateLineAsync(
        UpdateCartLineRequest request,
        CancellationToken cancellationToken);

    Task<Result<CartView>> RemoveLineAsync(
        RemoveCartLineRequest request,
        CancellationToken cancellationToken);

    /// <summary>純詢價：不得修改 Cart，也不得呼叫 Pricing.FreezeAsync。</summary>
    Task<Result<CheckoutQuote>> QuoteAsync(
        QuoteCartRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// 驗證並凍結報價，然後把 CheckoutCompleted 與 Cart 完成狀態寫進同一工作單元。
    /// BFF 可將回傳事件同步交給 Ordering 建單；outbox 的非同步重送會由 Ordering 冪等吸收。
    /// </summary>
    Task<Result<CheckoutCompleted>> CompleteAsync(
        CompleteCheckoutRequest request,
        CancellationToken cancellationToken);
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

    public override string AggregateType => "Cart";

    public override string AggregateId => CartId.ToString();

    /// <summary>超商取貨時由綠界電子地圖回傳；其他配送方式為 null。</summary>
    public string? ConvenienceStoreCode { get; init; }

    public string? BuyerNote { get; init; }
}

public sealed record CheckoutLine(
    SkuId SkuId,
    FulfillmentMode Mode,
    CampaignId? CampaignId,
    CampaignOfferId? CampaignOfferId,
    int Quantity,
    Money UnitPrice);
