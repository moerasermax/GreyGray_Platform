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
    /// <summary>收件人姓名的上限，對齊 <c>checkout.cart</c>／<c>ordering.orders</c> 的 varchar(50)。</summary>
    private const int RecipientNameMaxLength = 50;

    /// <summary>收件人手機的上限，對齊 varchar(20)。</summary>
    private const int RecipientPhoneMaxLength = 20;

    /// <summary>宅配地址單行字串的上限，對齊 varchar(200)。</summary>
    private const int RecipientAddressMaxLength = 200;

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

        var recipient = await ValidateDeliveryAsync(request, cancellationToken);
        if (recipient.IsFailure)
        {
            return Result<CheckoutCompleted>.Failure(recipient.Error);
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

        // ADR-030：出貨方式的規則主人是後端。混合購物車才問客人，單一模式一律推導。
        var shippingPolicy = ResolveShippingPolicy(
            validatedLines.Value.Select(line => line.Mode).ToArray(),
            request.ShippingPolicy);
        if (shippingPolicy.IsFailure)
        {
            return Result<CheckoutCompleted>.Failure(shippingPolicy.Error);
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
            shippingPolicy.Value,
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
            ConvenienceStoreName = NormalizeOptional(request.ConvenienceStoreName),
            ConvenienceStoreAddress = NormalizeOptional(request.ConvenienceStoreAddress),
            RecipientName = recipient.Value.Name,
            RecipientPhone = recipient.Value.Phone,
            RecipientAddress = recipient.Value.Address,
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

    /// <summary>
    /// 驗證配送方式，<b>順便把要凍結的收件人解析出來</b>（ADR-039）——宅配的收件人只有這裡
    /// 讀得到地址簿，分成兩次讀會多一次跨模組查詢，而且兩次之間客人就能改掉地址。
    /// </summary>
    private async Task<Result<ResolvedRecipient>> ValidateDeliveryAsync(
        CompleteCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var recipient = ResolvedRecipient.None;
        if (request.DeliveryMethod == DeliveryMethod.HomeDelivery)
        {
            if (request.ShippingAddressId is null)
            {
                return Result<ResolvedRecipient>.Failure(
                    "checkout.address-required",
                    "宅配必須選擇收件地址。");
            }

            var address = await customers.GetAddressAsync(
                request.ShippingAddressId.Value,
                cancellationToken);
            if (address.IsFailure || address.Value.CustomerId != request.CustomerId)
            {
                return Result<ResolvedRecipient>.Failure(
                    "checkout.address-not-found",
                    "找不到這個收件地址。");
            }

            // ADR-039：宅配的收件人<b>以地址簿為準</b>，在這裡抄一份凍結起來。
            // 刻意不叫客人在結帳頁再填一次（兩個地方各填一次一定會不一致），
            // 也刻意不採用請求裡送來的值——契約寫明宅配時那兩個欄位會被忽略。
            recipient = new ResolvedRecipient(
                NormalizeOptional(address.Value.RecipientName),
                NormalizeOptional(address.Value.PhoneNumber),
                FormatSingleLineAddress(address.Value));
        }
        else if (request.DeliveryMethod == DeliveryMethod.ConvenienceStore)
        {
            if (string.IsNullOrWhiteSpace(request.ConvenienceStoreCode))
            {
                return Result<ResolvedRecipient>.Failure(
                    "checkout.store-code-required",
                    "超商取貨必須選擇門市。");
            }

            // 超商核對證件，姓名不符會拒絕交貨；地址簿的收件人不見得是去超商領貨的人，
            // 所以這裡只認客人這一次填的值。
            var name = NormalizeOptional(request.RecipientName);
            var phone = NormalizeOptional(request.RecipientPhone);
            if (name is null || phone is null)
            {
                return Result<ResolvedRecipient>.Failure(
                    "checkout.recipient-required",
                    "超商取貨必須填寫收件人姓名與手機。");
            }

            // 地址留 null：超商取貨要寄到門市，門市資訊在 ConvenienceStore* 三個欄位。
            recipient = new ResolvedRecipient(name, phone, null);
        }
        else
        {
            // 面交／自取：契約沒有要求收件人，客人願意留就留下來，不強制；沒有收件地址。
            recipient = new ResolvedRecipient(
                NormalizeOptional(request.RecipientName),
                NormalizeOptional(request.RecipientPhone),
                null);
        }

        // 快照欄位是 varchar(50)／varchar(20)。超長要在這裡回業務失敗，
        // 不能讓它一路帶到 SaveChanges 才炸 DbUpdateException——那是 500。
        if (recipient.Name is { Length: > RecipientNameMaxLength })
        {
            return Result<ResolvedRecipient>.Failure(
                "checkout.recipient-name-too-long",
                $"收件人姓名不得超過 {RecipientNameMaxLength} 個字元。");
        }

        if (recipient.Phone is { Length: > RecipientPhoneMaxLength })
        {
            return Result<ResolvedRecipient>.Failure(
                "checkout.recipient-phone-too-long",
                $"收件人手機不得超過 {RecipientPhoneMaxLength} 個字元。");
        }

        // 地址簿的欄位在 Identity 是 text（加密後存），沒有資料庫層的長度上限，
        // 所以組出來的單行字串理論上可能超過 varchar(200)。真實地址遠短於此，
        // 但這裡仍要回業務失敗而不是<b>截斷</b>——被截斷的收件地址寄不到，
        // 而且沒有人會發現；回 422 客人改一下地址就能繼續買。
        if (recipient.Address is { Length: > RecipientAddressMaxLength })
        {
            return Result<ResolvedRecipient>.Failure(
                "checkout.recipient-address-too-long",
                $"收件地址不得超過 {RecipientAddressMaxLength} 個字元，請先修改地址簿。");
        }

        var enabled = await payments.GetEnabledProvidersAsync(cancellationToken);
        if (enabled.IsFailure)
        {
            return Result<ResolvedRecipient>.Failure(enabled.Error);
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
            ? recipient
            : Result<ResolvedRecipient>.Failure(
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
            HasMixedModes = HasMixedModes(lines.Select(line => line.Mode)),

            // #44：唯一的組裝點，所以只要在這裡帶值，每一條讀車的路都看得到。
            // 只加屬性不填就是「型別有了沒人填」——測試會綠，線上照樣壞。
            IsCompleted = cart.IsCompleted,
        };
    }

    /// <summary>
    /// 同時含現貨與預購。<c>CartView.HasMixedModes</c> 與結帳時「要不要問客人出貨方式」
    /// 用的是<b>同一個</b>定義——抄第二份就會有兩份各自漂移的規則（ADR-030）。
    /// </summary>
    private static bool HasMixedModes(IEnumerable<FulfillmentMode> modes) =>
        modes.Distinct().Count() > 1;

    /// <summary>
    /// ADR-030：決定這張單的出貨方式。純函式，不碰任何相依。
    /// <list type="bullet">
    /// <item>混合購物車：<paramref name="requested"/> 必填，缺了回
    /// <c>checkout.shipping-policy-required</c>（422）。</item>
    /// <item>單一模式：<b>忽略</b><paramref name="requested"/>（舊客戶端仍會送值，
    /// 不因此報錯），依 line 組成推導一個如實描述會發生什麼的值。</item>
    /// </list>
    /// </summary>
    internal static Result<ShippingPolicy> ResolveShippingPolicy(
        IReadOnlyCollection<FulfillmentMode> modes,
        ShippingPolicy? requested)
    {
        if (HasMixedModes(modes))
        {
            return requested is null
                ? Result<ShippingPolicy>.Failure(
                    "checkout.shipping-policy-required",
                    "同時有現貨與預購商品時，必須選擇出貨方式。")
                : requested.Value;
        }

        // 純現貨沒有什麼好等的，就是現貨先出；純預購一定是等回國一起出。
        return modes.Contains(FulfillmentMode.Preorder)
            ? ShippingPolicy.HoldUntilComplete
            : ShippingPolicy.ShipSeparately;
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

    /// <summary>
    /// 把地址簿的一筆地址組成後台出貨用的<b>單行字串</b>：
    /// <c>郵遞區號 空格 縣市鄉鎮市區街道</c>，例如 <c>100 臺北市中正區重慶南路一段122號</c>。
    /// 欄位之間不加多餘標點——後台是把它整行印出來貼在包裹上。
    /// </summary>
    private static string? FormatSingleLineAddress(ShippingAddress address)
    {
        var region = string.Concat(
            NormalizeOptional(address.City),
            NormalizeOptional(address.District),
            NormalizeOptional(address.StreetAddress));
        var postalCode = NormalizeOptional(address.PostalCode);
        if (region.Length == 0)
        {
            return postalCode;
        }

        return postalCode is null ? region : $"{postalCode} {region}";
    }

    /// <summary>下單當下要凍結進訂單的收件人（ADR-039）。每個值都已經 trim 過。</summary>
    private sealed record ResolvedRecipient(string? Name, string? Phone, string? Address)
    {
        public static ResolvedRecipient None { get; } = new(null, null, null);
    }

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
