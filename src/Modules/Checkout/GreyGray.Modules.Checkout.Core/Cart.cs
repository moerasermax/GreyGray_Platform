using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Shared.Kernel;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.Modules.Checkout.Core;

internal sealed class Cart
{
    private readonly List<CartLineEntity> _lines = [];

    private Cart()
    {
    }

    private Cart(CartId id, TenantId tenantId, CustomerId? customerId, DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        CustomerId = customerId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ShippingPolicy = ShippingPolicy.HoldUntilComplete;
    }

    public CartId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public CustomerId? CustomerId { get; private set; }

    public ShippingPolicy ShippingPolicy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? CompletedEventId { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? CheckoutIdempotencyKey { get; private set; }

    public PricingSnapshotId? CompletedPricingSnapshotId { get; private set; }

    public DeliveryMethod? CompletedDeliveryMethod { get; private set; }

    public AddressId? CompletedShippingAddressId { get; private set; }

    public string? CompletedConvenienceStoreCode { get; private set; }

    public string? CompletedConvenienceStoreName { get; private set; }

    public string? CompletedConvenienceStoreAddress { get; private set; }

    /// <summary>下單當下凍結的收件人姓名（ADR-039）。供 outbox 重送時重放事件用。</summary>
    public string? CompletedRecipientName { get; private set; }

    /// <summary>下單當下凍結的收件人手機（ADR-039）。</summary>
    public string? CompletedRecipientPhone { get; private set; }

    /// <summary>下單當下凍結的宅配收件地址單行字串（ADR-039）。超商取貨為 null。</summary>
    public string? CompletedRecipientAddress { get; private set; }

    public string? CompletedBuyerNote { get; private set; }

    public IReadOnlyList<CartLineEntity> Lines => _lines;

    public bool IsCompleted => CompletedEventId.HasValue;

    public static Cart Create(
        CartId id,
        TenantId tenantId,
        CustomerId? customerId,
        DateTimeOffset createdAt) =>
        new(id, tenantId, customerId, createdAt);

    public bool IsAccessibleBy(CustomerId? customerId) =>
        CustomerId is null || CustomerId == customerId;

    public void AddOrIncreaseLine(
        ValidatedCartLine line,
        int quantity,
        DateTimeOffset changedAt)
    {
        EnsureMutable();
        var existing = _lines.SingleOrDefault(candidate =>
            candidate.SkuId == line.Sku.Id
            && candidate.Mode == line.Mode
            && candidate.CampaignOfferId == line.CampaignOfferId);

        if (existing is null)
        {
            _lines.Add(CartLineEntity.Create(
                CartLineId.New(),
                Id,
                TenantId,
                line,
                quantity));
        }
        else
        {
            var combined = checked(existing.Quantity + quantity);
            if (combined > 999)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity));
            }

            existing.ChangeQuantity(combined, line.UnitPrice);
        }

        UpdatedAt = changedAt;
    }

    public bool UpdateLine(
        CartLineId lineId,
        int quantity,
        Money currentPrice,
        DateTimeOffset changedAt)
    {
        EnsureMutable();
        var line = _lines.SingleOrDefault(candidate => candidate.Id == lineId);
        if (line is null)
        {
            return false;
        }

        line.ChangeQuantity(quantity, currentPrice);
        UpdatedAt = changedAt;
        return true;
    }

    public bool RemoveLine(CartLineId lineId, DateTimeOffset changedAt)
    {
        EnsureMutable();
        var line = _lines.SingleOrDefault(candidate => candidate.Id == lineId);
        if (line is null)
        {
            return false;
        }

        _lines.Remove(line);
        UpdatedAt = changedAt;
        return true;
    }

    public void Complete(
        CustomerId customerId,
        CheckoutCompleted completed,
        string? convenienceStoreCode,
        string? buyerNote)
    {
        EnsureMutable();
        CustomerId = customerId;
        ShippingPolicy = completed.ShippingPolicy;
        CompletedEventId = completed.EventId;
        CompletedAt = completed.OccurredAt;
        CheckoutIdempotencyKey = completed.IdempotencyKey;
        CompletedPricingSnapshotId = completed.PricingSnapshotId;
        CompletedDeliveryMethod = completed.DeliveryMethod;
        CompletedShippingAddressId = completed.ShippingAddressId;
        CompletedConvenienceStoreCode = convenienceStoreCode;
        CompletedConvenienceStoreName = completed.ConvenienceStoreName;
        CompletedConvenienceStoreAddress = completed.ConvenienceStoreAddress;
        CompletedRecipientName = completed.RecipientName;
        CompletedRecipientPhone = completed.RecipientPhone;
        CompletedRecipientAddress = completed.RecipientAddress;
        CompletedBuyerNote = buyerNote;
        UpdatedAt = completed.OccurredAt;
    }

    public CheckoutCompleted ReplayCompletedEvent()
    {
        if (CompletedEventId is null
            || CompletedAt is null
            || CustomerId is null
            || CheckoutIdempotencyKey is null
            || CompletedPricingSnapshotId is null
            || CompletedDeliveryMethod is null)
        {
            throw new InvalidOperationException("購物車沒有完整的結帳事實，無法重放。");
        }

        return new CheckoutCompleted(
            CompletedEventId.Value,
            CompletedAt.Value,
            TenantId,
            Id,
            CustomerId.Value,
            CompletedShippingAddressId,
            CompletedDeliveryMethod.Value,
            ShippingPolicy,
            CompletedPricingSnapshotId.Value,
            _lines.Select(line => line.ToCheckoutLine()).ToArray(),
            CheckoutIdempotencyKey)
        {
            ConvenienceStoreCode = CompletedConvenienceStoreCode,
            ConvenienceStoreName = CompletedConvenienceStoreName,
            ConvenienceStoreAddress = CompletedConvenienceStoreAddress,
            RecipientName = CompletedRecipientName,
            RecipientPhone = CompletedRecipientPhone,
            RecipientAddress = CompletedRecipientAddress,
            BuyerNote = CompletedBuyerNote,
        };
    }

    private void EnsureMutable()
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("已完成結帳的購物車不可再修改。");
        }
    }
}

internal sealed class CartLineEntity
{
    private CartLineEntity()
    {
    }

    private CartLineEntity(
        CartLineId id,
        CartId cartId,
        TenantId tenantId,
        ValidatedCartLine line,
        int quantity)
    {
        Id = id;
        CartId = cartId;
        TenantId = tenantId;
        SkuId = line.Sku.Id;
        ProductId = line.Sku.ProductId;
        Name = line.Sku.Name;
        VariantName = line.Sku.VariantName;
        Mode = line.Mode;
        CampaignId = line.CampaignId;
        CampaignOfferId = line.CampaignOfferId;
        Quantity = quantity;
        UnitPriceAmountMinor = line.UnitPrice.AmountMinor;
        UnitPriceCurrency = line.UnitPrice.Currency;
    }

    public CartLineId Id { get; private set; }

    public CartId CartId { get; private set; }

    public TenantId TenantId { get; private set; }

    public SkuId SkuId { get; private set; }

    public ProductId ProductId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? VariantName { get; private set; }

    public FulfillmentMode Mode { get; private set; }

    public CampaignId? CampaignId { get; private set; }

    public CampaignOfferId? CampaignOfferId { get; private set; }

    public int Quantity { get; private set; }

    public long UnitPriceAmountMinor { get; private set; }

    public Currency UnitPriceCurrency { get; private set; }

    public Money UnitPrice => new(UnitPriceAmountMinor, UnitPriceCurrency);

    public static CartLineEntity Create(
        CartLineId id,
        CartId cartId,
        TenantId tenantId,
        ValidatedCartLine line,
        int quantity) =>
        new(id, cartId, tenantId, line, quantity);

    public void ChangeQuantity(int quantity, Money currentPrice)
    {
        Quantity = quantity;
        UnitPriceAmountMinor = currentPrice.AmountMinor;
        UnitPriceCurrency = currentPrice.Currency;
    }

    public CartLine ToContract(string? availabilityWarning = null) =>
        new(Id, SkuId, Mode, CampaignOfferId, Quantity, UnitPrice)
        {
            ProductId = ProductId,
            Name = Name,
            VariantName = VariantName,
            CampaignId = CampaignId,
            AvailabilityWarning = availabilityWarning,
        };

    public CheckoutLine ToCheckoutLine() =>
        new(SkuId, Mode, CampaignId, CampaignOfferId, Quantity, UnitPrice);
}

internal sealed record ValidatedCartLine(
    SkuSnapshot Sku,
    FulfillmentMode Mode,
    CampaignId? CampaignId,
    CampaignOfferId? CampaignOfferId,
    Money UnitPrice);

internal interface ICartRepository
{
    Task<Cart?> GetAsync(TenantId tenantId, CartId cartId, CancellationToken cancellationToken);

    void Add(Cart cart);
}
