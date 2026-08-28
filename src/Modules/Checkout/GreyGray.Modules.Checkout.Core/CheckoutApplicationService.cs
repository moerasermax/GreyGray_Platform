using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.Modules.Checkout.Core;

internal sealed class CheckoutApplicationService(
    ICartRepository carts,
    IUnitOfWork unitOfWork,
    IEventPublisher eventPublisher,
    ICatalogQuery catalog,
    ICampaignQuery campaigns,
    IInventoryQuery inventory,
    IPricingQuotation pricing,
    IPaymentQuery payments,
    ICustomerDirectory customers,
    IClock clock,
    ICorrelationContext correlationContext) : ICheckoutApplication, ICheckoutQuery
{
    public async Task<Result<CartView>> GetCartAsync(
        CartId cartId,
        CustomerId? customerId,
        CancellationToken cancellationToken)
    {
        var cart = await carts.GetAsync(correlationContext.TenantId, cartId, cancellationToken);
        if (cart is null)
        {
            return EmptyCart(cartId, customerId);
        }

        if (!cart.IsAccessibleBy(customerId))
        {
            return CartNotFound<CartView>();
        }

        return ToView(cart);
    }

    public Task<Result<CartView>> GetCartAsync(
        CartId id,
        CancellationToken cancellationToken) =>
        GetTrustedCartAsync(id, cancellationToken);

    public async Task<Result<CartView>> AddLineAsync(
        AddCartLineRequest request,
        CancellationToken cancellationToken)
    {
        var quantityError = ValidateQuantity(request.Quantity);
        if (quantityError is not null)
        {
            return Result<CartView>.Failure(quantityError);
        }

        var validated = await ValidateSellableAsync(
            request.SkuId,
            request.Mode,
            request.CampaignOfferId,
            request.Quantity,
            cancellationToken);
        if (validated.IsFailure)
        {
            return Result<CartView>.Failure(validated.Error);
        }

        var cart = await carts.GetAsync(
            correlationContext.TenantId,
            request.CartId,
            cancellationToken);
        if (cart is null)
        {
            cart = Cart.Create(
                request.CartId,
                correlationContext.TenantId,
                request.CustomerId,
                clock.UtcNow);
            carts.Add(cart);
        }
        else if (!cart.IsAccessibleBy(request.CustomerId))
        {
            return CartNotFound<CartView>();
        }
        else if (cart.IsCompleted)
        {
            return CartAlreadyCompleted<CartView>();
        }

        try
        {
            cart.AddOrIncreaseLine(validated.Value, request.Quantity, clock.UtcNow);
        }
        catch (Exception exception) when (
            exception is OverflowException or ArgumentOutOfRangeException)
        {
            return InvalidQuantity<CartView>();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToView(cart);
    }

    public async Task<Result<CartView>> UpdateLineAsync(
        UpdateCartLineRequest request,
        CancellationToken cancellationToken)
    {
        var quantityError = ValidateQuantity(request.Quantity);
        if (quantityError is not null)
        {
            return Result<CartView>.Failure(quantityError);
        }

        var cartResult = await GetOwnedMutableCartAsync(
            request.CartId,
            request.CustomerId,
            cancellationToken);
        if (cartResult.IsFailure)
        {
            return Result<CartView>.Failure(cartResult.Error);
        }

        var cart = cartResult.Value;
        var existing = cart.Lines.SingleOrDefault(line => line.Id == request.LineId);
        if (existing is null)
        {
            return Result<CartView>.Failure("checkout.cart-line-not-found", "找不到購物車品項。");
        }

        var validated = await ValidateSellableAsync(
            existing.SkuId,
            existing.Mode,
            existing.CampaignOfferId,
            request.Quantity,
            cancellationToken);
        if (validated.IsFailure)
        {
            return Result<CartView>.Failure(validated.Error);
        }

        cart.UpdateLine(request.LineId, request.Quantity, validated.Value.UnitPrice, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToView(cart);
    }

    public async Task<Result<CartView>> RemoveLineAsync(
        RemoveCartLineRequest request,
        CancellationToken cancellationToken)
    {
        var cartResult = await GetOwnedMutableCartAsync(
            request.CartId,
            request.CustomerId,
            cancellationToken);
        if (cartResult.IsFailure)
        {
            return Result<CartView>.Failure(cartResult.Error);
        }

        var cart = cartResult.Value;
        if (!cart.RemoveLine(request.LineId, clock.UtcNow))
        {
            return Result<CartView>.Failure("checkout.cart-line-not-found", "找不到購物車品項。");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToView(cart);
    }

    public async Task<Result<CheckoutQuote>> QuoteAsync(
        QuoteCartRequest request,
        CancellationToken cancellationToken)
    {
        var cart = await carts.GetAsync(
            correlationContext.TenantId,
            request.CartId,
            cancellationToken);
        if (cart is null || !cart.IsAccessibleBy(request.CustomerId))
        {
            return CartNotFound<CheckoutQuote>();
        }

        return await QuoteCartAsync(cart, request.CustomerId, request.DeliveryMethod, cancellationToken);
    }

    public async Task<Result<CheckoutCompleted>> CompleteAsync(
        CompleteCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = request.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 255)
        {
            return Result<CheckoutCompleted>.Failure(
                "platform.idempotency-key-required",
                "結帳必須提供 1 到 255 字元的 Idempotency-Key。");
        }

        var cart = await carts.GetAsync(
            correlationContext.TenantId,
            request.CartId,
            cancellationToken);
        if (cart is null || !cart.IsAccessibleBy(request.CustomerId))
        {
            return CartNotFound<CheckoutCompleted>();
        }

        if (cart.IsCompleted)
        {
            return StringComparer.Ordinal.Equals(cart.CheckoutIdempotencyKey, idempotencyKey)
                ? cart.ReplayCompletedEvent()
                : CartAlreadyCompleted<CheckoutCompleted>();
        }

        if (request.BuyerNote?.Length > 200)
        {
            return Result<CheckoutCompleted>.Failure(
                "checkout.buyer-note-too-long",
                "買家備註不得超過 200 個字元。");
        }

        var customer = await customers.GetAsync(request.CustomerId, cancellationToken);
        if (customer.IsFailure || !customer.Value.IsActive)
        {
            return Result<CheckoutCompleted>.Failure(
                "checkout.customer-not-active",
                "會員不存在或已停用，無法結帳。");
        }

        var deliveryValidation = await ValidateDeliveryAsync(request, cancellationToken);
        if (deliveryValidation.IsFailure)
        {
            return Result<CheckoutCompleted>.Failure(deliveryValidation.Error);
        }

        var quote = await QuoteCartAsync(
            cart,
            request.CustomerId,
            request.DeliveryMethod,
            cancellationToken);
        if (quote.IsFailure)
        {
            return Result<CheckoutCompleted>.Failure(quote.Error);
        }

        var frozen = await pricing.FreezeAsync(quote.Value.Snapshot, cancellationToken);
        if (frozen.IsFailure)
        {
            return Result<CheckoutCompleted>.Failure(frozen.Error);
        }

        var occurredAt = clock.UtcNow;
        var validatedLines = await ValidateAllLinesAsync(cart, cancellationToken);
        if (validatedLines.IsFailure)
        {
            return Result<CheckoutCompleted>.Failure(validatedLines.Error);
        }

        foreach (var line in validatedLines.Value)
        {
            cart.UpdateLine(line.CartLineId, line.Quantity, line.UnitPrice, occurredAt);
        }

        var completed = new CheckoutCompleted(
            Guid.CreateVersion7(),
            occurredAt,
            correlationContext.TenantId,
            cart.Id,
            request.CustomerId,
            request.ShippingAddressId,
            request.DeliveryMethod,
            request.ShippingPolicy,
            frozen.Value,
            validatedLines.Value.Select(line => new CheckoutLine(
                line.Sku.Id,
                line.Mode,
                line.CampaignId,
                line.CampaignOfferId,
                line.Quantity,
                line.UnitPrice)).ToArray(),
            idempotencyKey)
        {
            ConvenienceStoreCode = NormalizeOptional(request.ConvenienceStoreCode),
            BuyerNote = NormalizeOptional(request.BuyerNote),
        };

        cart.Complete(
            request.CustomerId,
            completed,
            completed.ConvenienceStoreCode,
            completed.BuyerNote);
        await eventPublisher.PublishAsync(completed, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return completed;
    }

    private async Task<Result<CheckoutQuote>> QuoteCartAsync(
        Cart cart,
        CustomerId? customerId,
        DeliveryMethod deliveryMethod,
        CancellationToken cancellationToken)
    {
        if (cart.Lines.Count == 0)
        {
            return Result<CheckoutQuote>.Failure("checkout.cart-empty", "購物車是空的，無法詢價。");
        }

        var validated = await ValidateAllLinesAsync(cart, cancellationToken);
        if (validated.IsFailure)
        {
            return Result<CheckoutQuote>.Failure(validated.Error);
        }

        var memberTier = MemberTier.Standard;
        if (customerId is not null)
        {
            var customer = await customers.GetAsync(customerId.Value, cancellationToken);
            if (customer.IsFailure || !customer.Value.IsActive)
            {
                return Result<CheckoutQuote>.Failure(
                    "checkout.customer-not-active",
                    "會員不存在或已停用，無法詢價。");
            }

            memberTier = customer.Value.Tier;
        }

        var quoteRequest = new QuoteRequest(
            deliveryMethod,
            validated.Value.Select(line => new QuoteLineRequest(
                line.Sku.Id,
                line.Quantity,
                line.Sku.WeightGram,
                line.Sku.Size)).ToArray(),
            customerId,
            memberTier);
        var snapshot = await pricing.QuoteAsync(quoteRequest, cancellationToken);
        if (snapshot.IsFailure)
        {
            return Result<CheckoutQuote>.Failure(snapshot.Error);
        }

        var goodsTotal = SumGoods(validated.Value);
        if (goodsTotal.IsFailure)
        {
            return Result<CheckoutQuote>.Failure(goodsTotal.Error);
        }

        try
        {
            return new CheckoutQuote(
                snapshot.Value,
                goodsTotal.Value,
                goodsTotal.Value + snapshot.Value.ShippingFee);
        }
        catch (InvalidOperationException)
        {
            return Result<CheckoutQuote>.Failure(
                "checkout.currency-mismatch",
                "商品與運費幣別不一致，無法結帳。");
        }
    }

    private async Task<Result<IReadOnlyList<ValidatedLineQuantity>>> ValidateAllLinesAsync(
        Cart cart,
        CancellationToken cancellationToken)
    {
        var result = new List<ValidatedLineQuantity>(cart.Lines.Count);
        foreach (var line in cart.Lines)
        {
            var validated = await ValidateSellableAsync(
                line.SkuId,
                line.Mode,
                line.CampaignOfferId,
                line.Quantity,
                cancellationToken);
            if (validated.IsFailure)
            {
                return Result<IReadOnlyList<ValidatedLineQuantity>>.Failure(validated.Error);
            }

            result.Add(new ValidatedLineQuantity(line.Id, validated.Value, line.Quantity));
        }

        return result;
    }

    private async Task<Result<ValidatedCartLine>> ValidateSellableAsync(
        SkuId skuId,
        FulfillmentMode mode,
        CampaignOfferId? campaignOfferId,
        int quantity,
        CancellationToken cancellationToken)
    {
        var sku = await catalog.GetSkuAsync(skuId, cancellationToken);
        if (sku.IsFailure)
        {
            return Result<ValidatedCartLine>.Failure(sku.Error);
        }

        if (!sku.Value.IsActive)
        {
            return Result<ValidatedCartLine>.Failure("catalog.sku-archived", "商品已下架。");
        }

        if (mode == FulfillmentMode.Stock)
        {
            var availability = await inventory.GetAvailabilityAsync([skuId], cancellationToken);
            if (availability.IsFailure)
            {
                return Result<ValidatedCartLine>.Failure(availability.Error);
            }

            var available = availability.Value.SingleOrDefault(item => item.SkuId == skuId)?.Available ?? 0;
            if (available < quantity)
            {
                return Result<ValidatedCartLine>.Failure(
                    "inventory.insufficient-stock",
                    "現貨庫存不足。");
            }

            if (sku.Value.ListPrice is null)
            {
                return Result<ValidatedCartLine>.Failure(
                    "checkout.stock-price-unavailable",
                    "現貨 SKU 尚未設定售價，無法加入購物車。");
            }

            return new ValidatedCartLine(
                sku.Value,
                mode,
                null,
                null,
                sku.Value.ListPrice.Value);
        }

        if (mode != FulfillmentMode.Preorder || campaignOfferId is null)
        {
            return Result<ValidatedCartLine>.Failure(
                "checkout.campaign-offer-required",
                "預購商品必須指定開團商品。");
        }

        var offer = await campaigns.GetOfferAsync(campaignOfferId.Value, cancellationToken);
        if (offer.IsFailure)
        {
            return Result<ValidatedCartLine>.Failure(offer.Error);
        }

        if (!offer.Value.IsActive || offer.Value.SkuId != skuId)
        {
            return Result<ValidatedCartLine>.Failure(
                "campaign.offer-not-sellable",
                "開團商品不存在、已停用或與 SKU 不符。");
        }

        var accepting = await campaigns.IsAcceptingOrdersAsync(
            offer.Value.CampaignId,
            cancellationToken);
        if (accepting.IsFailure)
        {
            return Result<ValidatedCartLine>.Failure(accepting.Error);
        }

        if (!accepting.Value)
        {
            return Result<ValidatedCartLine>.Failure(
                "campaign.not-accepting-orders",
                "這個團目前不接受下單。");
        }

        return new ValidatedCartLine(
            sku.Value,
            mode,
            offer.Value.CampaignId,
            offer.Value.Id,
            offer.Value.SellingPrice);
    }

    private async Task<Result> ValidateDeliveryAsync(
        CompleteCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DeliveryMethod == DeliveryMethod.HomeDelivery)
        {
            if (request.ShippingAddressId is null)
            {
                return Result.Failure("checkout.address-required", "宅配必須選擇收件地址。");
            }

            var address = await customers.GetAddressAsync(
                request.ShippingAddressId.Value,
                cancellationToken);
            if (address.IsFailure || address.Value.CustomerId != request.CustomerId)
            {
                return Result.Failure("checkout.address-not-found", "找不到這個收件地址。");
            }
        }

        if (request.DeliveryMethod == DeliveryMethod.ConvenienceStore
            && string.IsNullOrWhiteSpace(request.ConvenienceStoreCode))
        {
            return Result.Failure("checkout.store-code-required", "超商取貨必須選擇門市。");
        }

        var enabled = await payments.GetEnabledProvidersAsync(cancellationToken);
        if (enabled.IsFailure)
        {
            return Result.Failure(enabled.Error);
        }

        var ecPay = enabled.Value.SingleOrDefault(capability =>
            capability.Provider == PaymentProvider.ECPay);
        var supported = ecPay is not null && request.DeliveryMethod switch
        {
            DeliveryMethod.ConvenienceStore => ecPay.SupportsConvenienceStore,
            DeliveryMethod.HomeDelivery => ecPay.SupportsHomeDelivery,
            DeliveryMethod.SelfPickup => true,
            _ => false,
        };
        return supported
            ? Result.Success()
            : Result.Failure(
                "payment.provider-does-not-support-delivery-method",
                "目前啟用的金流不支援這種配送方式。");
    }

    private async Task<Result<Cart>> GetOwnedMutableCartAsync(
        CartId cartId,
        CustomerId? customerId,
        CancellationToken cancellationToken)
    {
        var cart = await carts.GetAsync(correlationContext.TenantId, cartId, cancellationToken);
        if (cart is null || !cart.IsAccessibleBy(customerId))
        {
            return CartNotFound<Cart>();
        }

        return cart.IsCompleted ? CartAlreadyCompleted<Cart>() : cart;
    }

    private async Task<Result<CartView>> GetTrustedCartAsync(
        CartId cartId,
        CancellationToken cancellationToken)
    {
        var cart = await carts.GetAsync(correlationContext.TenantId, cartId, cancellationToken);
        return cart is null ? CartNotFound<CartView>() : ToView(cart);
    }

    private static Result<CartView> ToView(Cart cart)
    {
        var lines = cart.Lines.Select(line => line.ToContract()).ToArray();
        var goods = SumGoods(lines.Select(line => new MoneyLine(line.UnitPrice, line.Quantity)));
        if (goods.IsFailure)
        {
            return Result<CartView>.Failure(goods.Error);
        }

        return new CartView(
            cart.Id,
            cart.CustomerId,
            lines,
            cart.CompletedDeliveryMethod,
            cart.ShippingPolicy,
            null)
        {
            GoodsTotal = goods.Value,
            HasMixedModes = lines.Select(line => line.Mode).Distinct().Count() > 1,
        };
    }

    private static CartView EmptyCart(CartId cartId, CustomerId? customerId) =>
        new(
            cartId,
            customerId,
            [],
            null,
            ShippingPolicy.HoldUntilComplete,
            null)
        {
            GoodsTotal = Money.Zero(Currency.TWD),
            HasMixedModes = false,
        };

    private static Result<Money> SumGoods(IEnumerable<ValidatedLineQuantity> lines) =>
        SumGoods(lines.Select(line => new MoneyLine(line.UnitPrice, line.Quantity)));

    private static Result<Money> SumGoods(IEnumerable<MoneyLine> lines)
    {
        Money? total = null;
        try
        {
            foreach (var line in lines)
            {
                var lineTotal = line.UnitPrice.MultiplyByQuantity(line.Quantity);
                total = total is null ? lineTotal : total.Value + lineTotal;
            }
        }
        catch (InvalidOperationException)
        {
            return Result<Money>.Failure(
                "checkout.currency-mismatch",
                "購物車內商品幣別不一致。");
        }

        return total ?? Money.Zero(Currency.TWD);
    }

    private static Error? ValidateQuantity(int quantity) =>
        quantity is < 1 or > 999
            ? new Error("checkout.invalid-quantity", "數量必須介於 1 到 999。")
            : null;

    private static Result<T> InvalidQuantity<T>() =>
        Result<T>.Failure("checkout.invalid-quantity", "數量必須介於 1 到 999。");

    private static Result<T> CartNotFound<T>() =>
        Result<T>.Failure("checkout.cart-not-found", "找不到購物車。");

    private static Result<T> CartAlreadyCompleted<T>() =>
        Result<T>.Failure("checkout.cart-already-completed", "購物車已完成結帳。");

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record MoneyLine(Money UnitPrice, int Quantity);

    private sealed record ValidatedLineQuantity(
        CartLineId CartLineId,
        ValidatedCartLine Line,
        int Quantity)
    {
        public SkuSnapshot Sku => Line.Sku;

        public FulfillmentMode Mode => Line.Mode;

        public CampaignId? CampaignId => Line.CampaignId;

        public CampaignOfferId? CampaignOfferId => Line.CampaignOfferId;

        public Money UnitPrice => Line.UnitPrice;
    }
}
