using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using GreyGray.Api.Storefront.Logistics;
using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ledger.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;
using Microsoft.Extensions.Caching.Distributed;

// BE-34：讓 CompleteCheckoutAsync 能被 CheckoutOrdering.Tests 直接呼叫。
// Admin Host 把同一條寫在 AssemblyInfo.cs，Storefront Host 沒有那個檔案。
[assembly: InternalsVisibleTo("GreyGray.M1a.CheckoutOrdering.Tests")]

// BE-39：讓 ListProductsAsync／GetProductDetailAsync 能被 IdentityCatalog.Tests 直接呼叫。
// 商品端點的 campaignId／campaign／price／campaignOfferId 四個欄位曾經整整一波
// 硬編碼成 null 而沒有任何測試發現，補測試需要看得到這兩個進入點與它們的回應型別。
[assembly: InternalsVisibleTo("GreyGray.M1a.IdentityCatalog.Tests")]

namespace GreyGray.Api.Storefront;

internal static class M1aEndpoints
{
    private const string SessionCookie = "gg_session";
    private const string CartCookie = "gg_cart";

    public static IEndpointRouteBuilder MapM1aStorefrontEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/v1");
        MapAuth(api);
        MapMe(api);
        MapCatalog(api);
        MapCampaign(api);
        MapCart(api);
        CvsLogisticsEndpoints.Map(api);
        MapOrders(api);
        MapPayment(api);
        return endpoints;
    }

    private static void MapAuth(RouteGroupBuilder api)
    {
        api.MapPost("/auth/register", async (
            RegisterCustomerInput input,
            HttpContext context,
            ICustomerAccounts accounts,
            ISessionStore sessions,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                "storefront:auth:register",
                input,
                async token =>
                {
                    var registered = await accounts.RegisterAsync(input, token);
                    if (registered.IsFailure)
                    {
                        return Result<CustomerMe>.Failure(registered.Error);
                    }

                    var me = ToMe(registered.Value);
                    await CreateSessionAsync(context, sessions, me, token);
                    return me;
                },
                StatusCodes.Status201Created,
                cancellationToken,
                (me, token) => CreateSessionAsync(context, sessions, me, token)));

        api.MapPost("/auth/login", async (
            CustomerLoginInput input,
            HttpContext context,
            ICustomerAccounts accounts,
            ISessionStore sessions,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                "storefront:auth:login",
                input,
                async token =>
                {
                    var authenticated = await accounts.AuthenticateAsync(input, token);
                    if (authenticated.IsFailure)
                    {
                        return Result<CustomerMe>.Failure(authenticated.Error);
                    }

                    var me = ToMe(authenticated.Value);
                    await CreateSessionAsync(context, sessions, me, token);
                    return me;
                },
                StatusCodes.Status200OK,
                cancellationToken,
                (me, token) => CreateSessionAsync(context, sessions, me, token)));

        api.MapPost("/auth/logout", LogoutAsync);
    }

    private static void MapMe(RouteGroupBuilder api)
    {
        api.MapGet("/me", async (
            HttpContext context,
            ISessionStore sessions,
            ICustomerAccounts accounts,
            CancellationToken cancellationToken) =>
        {
            var customer = await GetCustomerAsync(context, sessions, cancellationToken);
            if (customer is null)
            {
                return BffHttp.Unauthorized();
            }

            var result = await accounts.GetProfileAsync(customer.Value, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(ToMe(result.Value))
                : BffHttp.Problem(result.Error);
        });

        api.MapPatch("/me", async (
            UpdateCustomerProfileInput input,
            HttpContext context,
            ISessionStore sessions,
            ICustomerAccounts accounts,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            var customer = await GetCustomerAsync(context, sessions, cancellationToken);
            if (customer is null)
            {
                return BffHttp.Unauthorized();
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                $"storefront:{customer}:me:update",
                input,
                async token =>
                {
                    var updated = await accounts.UpdateProfileAsync(customer.Value, input, token);
                    return updated.IsSuccess
                        ? Result<CustomerMe>.Success(ToMe(updated.Value))
                        : Result<CustomerMe>.Failure(updated.Error);
                },
                StatusCodes.Status200OK,
                cancellationToken);
        });

        api.MapGet("/me/addresses", async (
            HttpContext context,
            ISessionStore sessions,
            ICustomerAddressBook addresses,
            CancellationToken cancellationToken) =>
        {
            var customer = await GetCustomerAsync(context, sessions, cancellationToken);
            if (customer is null)
            {
                return BffHttp.Unauthorized();
            }

            var result = await addresses.ListAsync(customer.Value, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : BffHttp.Problem(result.Error);
        });

        api.MapPost("/me/addresses", async (
            ShippingAddressInput input,
            HttpContext context,
            ISessionStore sessions,
            ICustomerAddressBook addresses,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            var customer = await GetCustomerAsync(context, sessions, cancellationToken);
            if (customer is null)
            {
                return BffHttp.Unauthorized();
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                $"storefront:{customer}:addresses:add",
                input,
                token => addresses.AddAsync(customer.Value, input, token),
                StatusCodes.Status201Created,
                cancellationToken);
        });

        api.MapPut("/me/addresses/{addressId}", async (
            string addressId,
            ShippingAddressInput input,
            HttpContext context,
            ISessionStore sessions,
            ICustomerAddressBook addresses,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            var customer = await GetCustomerAsync(context, sessions, cancellationToken);
            if (customer is null)
            {
                return BffHttp.Unauthorized();
            }

            if (!TryId(addressId, out var id))
            {
                return BffHttp.Problem(new Error("identity.address-not-found", "找不到地址。"));
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                $"storefront:{customer}:addresses:{addressId}:update",
                input,
                token => addresses.UpdateAsync(customer.Value, new AddressId(id), input, token),
                StatusCodes.Status200OK,
                cancellationToken);
        });

        api.MapDelete("/me/addresses/{addressId}", async (
            string addressId,
            HttpContext context,
            ISessionStore sessions,
            ICustomerAddressBook addresses,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            var customer = await GetCustomerAsync(context, sessions, cancellationToken);
            if (customer is null)
            {
                return BffHttp.Unauthorized();
            }

            if (!TryId(addressId, out var id))
            {
                return BffHttp.Problem(new Error("identity.address-not-found", "找不到地址。"));
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                $"storefront:{customer}:addresses:{addressId}:delete",
                request: null,
                token => addresses.DeleteAsync(customer.Value, new AddressId(id), token),
                cancellationToken);
        });

        api.MapGet("/me/stored-value", async (
            HttpContext context,
            ISessionStore sessions,
            IStoredValueQuery storedValue,
            CancellationToken cancellationToken) =>
        {
            var customer = await GetCustomerAsync(context, sessions, cancellationToken);
            if (customer is null)
            {
                return BffHttp.Unauthorized();
            }

            var result = await storedValue.GetBalanceAsync(customer.Value, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new StoredValueResponse(result.Value))
                : BffHttp.Problem(result.Error);
        });

        api.MapGet("/me/favorites", GetFavoritesAsync);
        api.MapPut("/me/favorites/{productId}", PutFavoriteAsync);
        api.MapDelete("/me/favorites/{productId}", DeleteFavoriteAsync);
    }

    internal static async Task<IResult> GetFavoritesAsync(
        HttpContext context,
        ISessionStore sessions,
        IStorefrontFavorites favorites,
        ICampaignStorefront campaigns,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        var customer = await GetCustomerAsync(context, sessions, cancellationToken);
        if (customer is null)
        {
            return BffHttp.Unauthorized();
        }

        var result = await favorites.ListAsync(customer.Value, cursor, limit ?? 20, cancellationToken);
        if (result.IsFailure)
        {
            return BffHttp.Problem(result.Error);
        }

        var favorited = result.Value.Items.Select(item => item.Id).ToHashSet();
        var response = await ProjectProductPageAsync(result.Value, favorited, campaigns, cancellationToken);
        return response.IsSuccess ? Results.Ok(response.Value) : BffHttp.Problem(response.Error);
    }

    internal static async Task<IResult> PutFavoriteAsync(
        string productId,
        HttpContext context,
        ISessionStore sessions,
        IStorefrontFavorites favorites,
        IIdempotencyStore idempotency,
        CancellationToken cancellationToken)
    {
        var customer = await GetCustomerAsync(context, sessions, cancellationToken);
        if (customer is null)
        {
            return BffHttp.Unauthorized();
        }

        if (!TryId(productId, out var parsed))
        {
            return BffHttp.Problem(new Error("catalog.product-not-found", "找不到商品。"));
        }

        var id = new ProductId(parsed);
        return await BffHttp.ExecuteIdempotentAsync(
            context,
            idempotency,
            $"storefront:{customer}:favorites:{productId}:put",
            id,
            token => favorites.AddAsync(customer.Value, id, token),
            cancellationToken);
    }

    internal static async Task<IResult> DeleteFavoriteAsync(
        string productId,
        HttpContext context,
        ISessionStore sessions,
        IStorefrontFavorites favorites,
        IIdempotencyStore idempotency,
        CancellationToken cancellationToken)
    {
        var customer = await GetCustomerAsync(context, sessions, cancellationToken);
        if (customer is null)
        {
            return BffHttp.Unauthorized();
        }

        if (!TryId(productId, out var parsed))
        {
            return BffHttp.Problem(new Error("catalog.product-not-found", "找不到商品。"));
        }

        var id = new ProductId(parsed);
        return await BffHttp.ExecuteIdempotentAsync(
            context,
            idempotency,
            $"storefront:{customer}:favorites:{productId}:delete",
            request: null,
            token => favorites.RemoveAsync(customer.Value, id, token),
            cancellationToken);
    }

    private static void MapCatalog(RouteGroupBuilder api)
    {
        api.MapGet("/categories", async (
            IStorefrontCatalogQuery catalog,
            CancellationToken cancellationToken) =>
            Results.Ok((await catalog.ListCategoriesAsync(cancellationToken))
                .Select(category => new CategoryResponse(category.Id, category.Name, category.ImageUrl))
                .ToArray()));

        api.MapGet("/products", async (
            string? q,
            string? categoryId,
            FulfillmentMode? mode,
            string? cursor,
            int? limit,
            HttpContext context,
            ISessionStore sessions,
            IStorefrontCatalogQuery catalog,
            IStorefrontFavorites favorites,
            ICampaignStorefront campaigns,
            CancellationToken cancellationToken) =>
        {
            CategoryId? category = null;
            if (categoryId is not null)
            {
                if (!TryId(categoryId, out var parsed))
                {
                    return BffHttp.Problem(new Error("catalog.invalid-category-id", "分類識別格式錯誤。"));
                }

                category = new CategoryId(parsed);
            }

            var result = await ListProductsAsync(
                new ProductSearch(q, category, mode, false, cursor, limit ?? 20),
                await GetCustomerAsync(context, sessions, cancellationToken),
                catalog,
                favorites,
                campaigns,
                cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : BffHttp.Problem(result.Error);
        });

        api.MapGet("/products/{productId}", async (
            string productId,
            HttpContext context,
            ISessionStore sessions,
            IStorefrontCatalogQuery catalog,
            IStorefrontFavorites favorites,
            IInventoryQuery inventory,
            ICampaignStorefront campaigns,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(productId, out var parsed))
            {
                return BffHttp.Problem(new Error("catalog.product-not-found", "找不到商品。"));
            }

            var result = await GetProductDetailAsync(
                new ProductId(parsed),
                await GetCustomerAsync(context, sessions, cancellationToken),
                catalog,
                favorites,
                inventory,
                campaigns,
                cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : BffHttp.Problem(result.Error);
        });
    }

    /// <summary>
    /// 組 <c>GET /v1/products</c> 的回應。
    /// </summary>
    /// <remarks>
    /// <para>
    /// BE-39：預購商品的 <c>campaignId</c> 與 <c>priceFrom</c> 只能從開團來——Catalog 那邊
    /// 預購 SKU 沒有標價，所以在補這一段之前，前台商品列表對每一個預購商品都顯示
    /// 「目前無法購買」，而同一個商品在開團頁是可以買的。
    /// </para>
    /// <para>
    /// <b>先一次查完整頁，再投影</b>：在 <c>Select</c> 裡逐一 <c>await</c> 就會變 N+1。
    /// 整頁都是現貨時完全不問 Campaign。
    /// </para>
    /// </remarks>
    internal static async Task<Result<ProductPageResponse>> ListProductsAsync(
        ProductSearch search,
        CustomerId? customerId,
        IStorefrontCatalogQuery catalog,
        IStorefrontFavorites favorites,
        ICampaignStorefront campaigns,
        CancellationToken cancellationToken)
    {
        var result = await catalog.ListProductsAsync(search, cancellationToken);
        if (result.IsFailure)
        {
            return Result<ProductPageResponse>.Failure(result.Error);
        }

        IReadOnlySet<ProductId> favorited = customerId is null
            ? new HashSet<ProductId>()
            : await favorites.FindAsync(
                customerId.Value,
                result.Value.Items.Select(item => item.Id).ToArray(),
                cancellationToken);
        return await ProjectProductPageAsync(result.Value, favorited, campaigns, cancellationToken);
    }

    private static async Task<Result<ProductPageResponse>> ProjectProductPageAsync(
        CursorPage<StorefrontProductListItem> page,
        IReadOnlySet<ProductId> favorited,
        ICampaignStorefront campaigns,
        CancellationToken cancellationToken)
    {
        var pricing = await FindCampaignPricingAsync(
            campaigns,
            page.Items
                .Where(item => item.Mode == FulfillmentMode.Preorder)
                .Select(item => item.Id)
                .ToArray(),
            cancellationToken);
        return pricing.IsFailure
            ? Result<ProductPageResponse>.Failure(pricing.Error)
            : new ProductPageResponse(
                page.Items
                    .Select(item => ToProductListItem(
                        item,
                        pricing.Value.GetValueOrDefault(item.Id),
                        favorited.Contains(item.Id)))
                    .ToArray(),
                page.NextCursor);
    }

    /// <summary>
    /// 組 <c>GET /v1/products/{productId}</c> 的回應。
    /// </summary>
    /// <remarks>
    /// BE-39：預購商品要附上團的摘要（前端要顯示截團倒數，也拿 <c>isAcceptingOrders</c>
    /// 判斷能不能下單），每個 SKU 要附上該團的凍結售價與 <c>campaignOfferId</c>。
    /// </remarks>
    internal static async Task<Result<ProductDetailResponse>> GetProductDetailAsync(
        ProductId productId,
        CustomerId? customerId,
        IStorefrontCatalogQuery catalog,
        IStorefrontFavorites favorites,
        IInventoryQuery inventory,
        ICampaignStorefront campaigns,
        CancellationToken cancellationToken)
    {
        var result = await catalog.GetProductAsync(productId, cancellationToken);
        if (result.IsFailure)
        {
            return Result<ProductDetailResponse>.Failure(result.Error);
        }

        var isFavorited = customerId is not null &&
            (await favorites.FindAsync(customerId.Value, [productId], cancellationToken)).Contains(productId);
        ProductId[] preorder = result.Value.Mode == FulfillmentMode.Preorder
            ? [result.Value.Id]
            : [];
        var pricing = await FindCampaignPricingAsync(campaigns, preorder, cancellationToken);
        return pricing.IsFailure
            ? Result<ProductDetailResponse>.Failure(pricing.Error)
            : await ToProductDetailAsync(
                result.Value,
                inventory,
                pricing.Value.GetValueOrDefault(result.Value.Id),
                isFavorited,
                cancellationToken);
    }

    /// <summary>沒有任何預購商品時不去打 Campaign 模組；有的話一次問完。</summary>
    private static async Task<Result<IReadOnlyDictionary<ProductId, StorefrontProductCampaign>>>
        FindCampaignPricingAsync(
            ICampaignStorefront campaigns,
            IReadOnlyCollection<ProductId> preorderProductIds,
            CancellationToken cancellationToken) =>
        preorderProductIds.Count == 0
            ? Result<IReadOnlyDictionary<ProductId, StorefrontProductCampaign>>.Success(
                ReadOnlyDictionary<ProductId, StorefrontProductCampaign>.Empty)
            : await campaigns.FindOpenCampaignPricingAsync(preorderProductIds, cancellationToken);

    private static void MapCampaign(RouteGroupBuilder api)
    {
        api.MapGet("/campaigns", async (
            CampaignStatus? status,
            string? cursor,
            int? limit,
            ICampaignStorefront campaigns,
            CancellationToken cancellationToken) =>
        {
            var result = await campaigns.ListAsync(
                new CampaignPageRequest(status, cursor, limit ?? 20),
                cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : BffHttp.Problem(result.Error);
        });

        api.MapGet("/campaigns/{campaignId}", async (
            string campaignId,
            ICampaignStorefront campaigns,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(campaignId, out var parsed))
            {
                return BffHttp.Problem(new Error("campaign.not-found", "找不到指定的開團。"));
            }

            var result = await campaigns.GetDetailAsync(new CampaignId(parsed), cancellationToken);
            return result.IsSuccess
                ? Results.Ok(ToCampaignDetail(result.Value))
                : BffHttp.Problem(result.Error);
        });
    }

    private static void MapCart(RouteGroupBuilder api)
    {
        var logger = LoggerFor(api);

        api.MapGet("/cart", GetCartAsync);

        api.MapPost("/cart/lines", AddCartLineAsync);

        api.MapPatch("/cart/lines/{lineId}", async (
            string lineId,
            UpdateCartLineInput input,
            HttpContext context,
            ISessionStore sessions,
            ICheckoutApplication checkout,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(lineId, out var parsed))
            {
                return BffHttp.Problem(new Error("checkout.cart-line-not-found", "找不到購物車品項。"));
            }

            var request = new UpdateCartLineRequest(
                GetOrCreateCartId(context),
                await GetCustomerAsync(context, sessions, cancellationToken),
                new CartLineId(parsed),
                input.Quantity);
            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                "storefront:cart:lines:update",
                request,
                async token =>
                {
                    var updated = await checkout.UpdateLineAsync(request, token);
                    return updated.IsSuccess
                        ? Result<CartResponse>.Success(ToCart(updated.Value))
                        : Result<CartResponse>.Failure(updated.Error);
                },
                StatusCodes.Status200OK,
                cancellationToken);
        });

        api.MapDelete("/cart/lines/{lineId}", async (
            string lineId,
            HttpContext context,
            ISessionStore sessions,
            ICheckoutApplication checkout,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(lineId, out var parsed))
            {
                return BffHttp.Problem(new Error("checkout.cart-line-not-found", "找不到購物車品項。"));
            }

            var request = new RemoveCartLineRequest(
                GetOrCreateCartId(context),
                await GetCustomerAsync(context, sessions, cancellationToken),
                new CartLineId(parsed));
            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                "storefront:cart:lines:remove",
                request,
                async token =>
                {
                    var removed = await checkout.RemoveLineAsync(request, token);
                    return removed.IsSuccess
                        ? Result<CartResponse>.Success(ToCart(removed.Value))
                        : Result<CartResponse>.Failure(removed.Error);
                },
                StatusCodes.Status200OK,
                cancellationToken);
        });

        api.MapPost("/cart/quote", async (
            QuoteCartInput input,
            HttpContext context,
            ISessionStore sessions,
            ICheckoutApplication checkout,
            CancellationToken cancellationToken) =>
        {
            var request = new QuoteCartRequest(
                GetOrCreateCartId(context),
                await GetCustomerAsync(context, sessions, cancellationToken),
                input.DeliveryMethod);
            var quote = await checkout.QuoteAsync(request, cancellationToken);
            return quote.IsSuccess
                ? Results.Ok(ToQuote(quote.Value))
                : BffHttp.Problem(quote.Error);
        });

        api.MapPost("/cart/checkout", async (
            CompleteCheckoutInput input,
            HttpContext context,
            ISessionStore sessions,
            ICheckoutApplication checkout,
            IOrderingApplication ordering,
            ICatalogQuery catalog,
            ICustomerDirectory customers,
            IIdempotencyStore idempotency,
            IDistributedCache cache,
            IClock clock,
            CancellationToken cancellationToken) =>
            await CompleteCheckoutAsync(
                input,
                context,
                sessions,
                checkout,
                ordering,
                catalog,
                customers,
                idempotency,
                cache,
                clock,
                logger,
                cancellationToken));
    }

    /// <summary>
    /// <c>POST /v1/cart/checkout</c> 的處理邏輯。BE-34 從 <see cref="MapCart"/> 的 inline
    /// lambda 原樣抽出來，比照 Admin Host 的 <c>CancelOrderLineAsync</c>，好讓測試直接呼叫。
    /// </summary>
    /// <remarks>
    /// BE-35：改用 <see cref="BffHttp.ExecuteIdempotentAsync{TState, TResponse}"/> 兩階段多載。
    /// ①<c>CompleteAsync</c> ②<c>CreateFromCheckoutAsync</c> 屬於 <c>work</c>（失敗時副作用還沒
    /// 產生、或產生了但底層可重入，abandon 是安全的）；③<see cref="ToOrderAsync"/> 屬於
    /// <c>render</c>，它<b>不會失敗</b>——訂單這時已經 commit 了，讓組回應把它報成失敗
    /// 正是缺陷 (A′)。
    /// </remarks>
    internal static async Task<IResult> CompleteCheckoutAsync(
        CompleteCheckoutInput input,
        HttpContext context,
        ISessionStore sessions,
        ICheckoutApplication checkout,
        IOrderingApplication ordering,
        ICatalogQuery catalog,
        ICustomerDirectory customers,
        IIdempotencyStore idempotency,
        IDistributedCache cache,
        IClock clock,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var customer = await GetCustomerAsync(context, sessions, cancellationToken);
        if (customer is null)
        {
            return BffHttp.Unauthorized();
        }

        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var key))
        {
            return BffHttp.Problem(
                new Error("platform.idempotency-key-required", "缺少 Idempotency-Key header。"),
                StatusCodes.Status400BadRequest);
        }

        var cartId = GetOrCreateCartId(context);
        var request = new CompleteCheckoutRequest(
            cartId,
            customer.Value,
            input.DeliveryMethod,
            input.ShippingPolicy,
            input.ShippingAddressId,
            input.ConvenienceStoreCode,
            input.BuyerNote,
            key.ToString().Trim())
        {
            // ADR-039：宅配時 Checkout 會忽略這兩個值、改從地址簿抄，所以這裡原樣帶過去就好，
            // BFF 不做業務判斷。
            RecipientName = input.RecipientName,
            RecipientPhone = input.RecipientPhone,
        };
        var fingerprint = new CompleteCheckoutFingerprint(
            cartId,
            customer.Value,
            input.DeliveryMethod,
            input.ShippingPolicy,
            input.ShippingAddressId,
            input.ConvenienceStoreSelectionId,
            input.ConvenienceStoreCode,
            input.RecipientName,
            input.RecipientPhone,
            input.BuyerNote);
        var result = await BffHttp.ExecuteIdempotentAsync(
            context,
            idempotency,
            "storefront:cart:checkout",
            fingerprint,
            async token =>
            {
                var effectiveRequest = request;
                if (input.DeliveryMethod == DeliveryMethod.ConvenienceStore
                    && input.ConvenienceStoreSelectionId is not null)
                {
                    var selected = await CvsLogisticsEndpoints.ResolveForCheckoutAsync(
                        input.ConvenienceStoreSelectionId,
                        cartId,
                        cache,
                        clock,
                        token);
                    if (selected.IsFailure)
                    {
                        return Result<OrderView>.Failure(selected.Error);
                    }

                    effectiveRequest = request with
                    {
                        ConvenienceStoreCode = selected.Value.StoreCode,
                        ConvenienceStoreName = selected.Value.StoreName,
                        ConvenienceStoreAddress = selected.Value.StoreAddress,
                    };
                }

                var completed = await checkout.CompleteAsync(effectiveRequest, token);
                if (completed.IsFailure)
                {
                    // #44 必做 B：cookie 指到的車已經下過單了。錯誤碼不動（前端與既有測試都認它），
                    // 但換一顆新 cookie 並把訊息改成講得出下一步的話——原本的「購物車已完成結帳。」
                    // 沒有出口，正式機上使用者就這樣連撞 15 次 422。
                    // 這裡<b>不</b>拿新車重試 checkout：新車是空的，重試只會換成另一種錯誤。
                    if (completed.Error.Code == "checkout.cart-already-completed")
                    {
                        SetCartCookie(context.Response, CartId.New());
                        return Result<OrderView>.Failure(
                            "checkout.cart-already-completed",
                            "這台購物車已經下過單了，已經幫你換一台新的，請重新加入商品。");
                    }

                    return Result<OrderView>.Failure(completed.Error);
                }

                return await ordering.CreateFromCheckoutAsync(completed.Value, token);
            },
            (order, token) => ToOrderAsync(order, catalog, customers, logger, token),
            StatusCodes.Status201Created,
            cancellationToken);

        // #44 必做 A：訂單成立了，那台車再也不能用。不換的話 gg_cart 一直指著已結案的車，
        // 前台購物車與徽章不會歸零，而再按一次「送出訂單」就是 422（正式機連續 15 次）。
        // 判斷放在<b>方法回傳之前</b>而不是 work 裡面：ExecuteIdempotentAsync 兩階段多載在
        // 同一把 Idempotency-Key 重放時<b>根本不會進 work</b>（直接回快取），寫在 work 裡
        // 重放就換不到車。用回應狀態判斷則兩條路都涵蓋，而且重放時再換一次也無害——
        // 換到的一樣是一台全新的空車。
        if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status201Created })
        {
            SetCartCookie(context.Response, CartId.New());
        }

        return result;
    }

    private static void MapOrders(RouteGroupBuilder api)
    {
        var logger = LoggerFor(api);

        api.MapGet("/orders", async (
            OrderStatus? status,
            string? cursor,
            int? limit,
            HttpContext context,
            ISessionStore sessions,
            IOrderingApplication ordering,
            CancellationToken cancellationToken) =>
        {
            var customer = await GetCustomerAsync(context, sessions, cancellationToken);
            if (customer is null)
            {
                return BffHttp.Unauthorized();
            }

            OrderId? after = null;
            if (cursor is not null)
            {
                if (!TryId(cursor, out var parsed))
                {
                    return BffHttp.Problem(new Error("ordering.invalid-cursor", "游標格式錯誤。"));
                }

                after = new OrderId(parsed);
            }

            var result = await ordering.ListCustomerAsync(
                new CustomerOrderListRequest(customer.Value, status, after, limit ?? 20),
                cancellationToken);
            if (result.IsFailure)
            {
                return BffHttp.Problem(result.Error);
            }

            var items = new List<OrderListItemResponse>(result.Value.Items.Count);
            foreach (var order in result.Value.Items)
            {
                items.Add(ToOrderListItem(order));
            }

            return Results.Ok(new OrderPageResponse(items, result.Value.NextCursor));
        });

        api.MapGet("/orders/{orderId}", async (
            string orderId,
            HttpContext context,
            ISessionStore sessions,
            IOrderingApplication ordering,
            ICatalogQuery catalog,
            ICustomerDirectory customers,
            CancellationToken cancellationToken) =>
        {
            var customer = await GetCustomerAsync(context, sessions, cancellationToken);
            if (customer is null)
            {
                return BffHttp.Unauthorized();
            }

            if (!TryId(orderId, out var parsed))
            {
                return BffHttp.Problem(new Error("ordering.order-not-found", "找不到訂單。"));
            }

            var result = await ordering.GetCustomerAsync(customer.Value, new OrderId(parsed), cancellationToken);
            if (result.IsFailure)
            {
                return BffHttp.Problem(result.Error);
            }

            // BE-35：ToOrderAsync 不再會失敗，讀不到 SKU 時回退化值並留下 log。
            return Results.Ok(
                await ToOrderAsync(result.Value, catalog, customers, logger, cancellationToken));
        });

        api.MapPost("/orders/{orderId}/cancel", async (
            string orderId,
            CancelOrderInput? input,
            HttpContext context,
            ISessionStore sessions,
            IOrderingApplication ordering,
            ICatalogQuery catalog,
            ICustomerDirectory customers,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await CancelCustomerOrderAsync(
                orderId,
                input,
                context,
                sessions,
                ordering,
                catalog,
                customers,
                idempotency,
                logger,
                cancellationToken));
    }

    /// <summary>
    /// <c>POST /v1/orders/{orderId}/cancel</c> 的處理邏輯（BE-34 分類表的 S12）。
    /// BE-35 比照 <see cref="CompleteCheckoutAsync"/> 從 inline lambda 抽出來，好讓測試直接呼叫。
    /// </summary>
    /// <remarks>
    /// 這一條比 checkout 更糟：<c>CancelCustomerAsync</c> commit 之後 <c>OrderCancelled</c>／
    /// <c>RefundRequested</c> 就已經送出去了，而底層是狀態機守衛（訂單已 <c>Cancelled</c> 就擋下），
    /// 所以「組回應失敗 → abandon → 重試」<b>回不了成功</b>——退款已經發生，客人卻只看得到錯誤。
    /// 改用兩階段多載之後，<see cref="ToOrderAsync"/> 讀不到 SKU 也不會把已經完成的取消報成失敗。
    /// </remarks>
    internal static async Task<IResult> CancelCustomerOrderAsync(
        string orderId,
        CancelOrderInput? input,
        HttpContext context,
        ISessionStore sessions,
        IOrderingApplication ordering,
        ICatalogQuery catalog,
        ICustomerDirectory customers,
        IIdempotencyStore idempotency,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var customer = await GetCustomerAsync(context, sessions, cancellationToken);
        if (customer is null)
        {
            return BffHttp.Unauthorized();
        }

        if (!TryId(orderId, out var parsed))
        {
            return BffHttp.Problem(new Error("ordering.order-not-found", "找不到訂單。"));
        }

        var request = new CancelCustomerOrderRequest(customer.Value, new OrderId(parsed), input?.Reason);
        return await BffHttp.ExecuteIdempotentAsync(
            context,
            idempotency,
            "storefront:orders:cancel",
            request,
            token => ordering.CancelCustomerAsync(
                request.CustomerId,
                request.OrderId,
                request.Reason,
                token),
            (order, token) => ToOrderAsync(order, catalog, customers, logger, token),
            StatusCodes.Status200OK,
            cancellationToken);
    }

    private static void MapPayment(RouteGroupBuilder api)
    {
        api.MapPost("/orders/{orderId}/payment", async (
            string orderId,
            HttpContext context,
            ISessionStore sessions,
            IOrderingApplication ordering,
            IPaymentCommand payments,
            IIdempotencyStore idempotency,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var customer = await GetCustomerAsync(context, sessions, cancellationToken);
            if (customer is null)
            {
                return BffHttp.Unauthorized();
            }

            if (!TryId(orderId, out var parsed))
            {
                return BffHttp.Problem(new Error("ordering.order-not-found", "找不到訂單。"));
            }

            var id = new OrderId(parsed);
            var clientBackUrl = BuildPaymentResultUrl(configuration, parsed);
            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                "storefront:orders:payment",
                new { customerId = customer.Value, orderId = id },
                async token =>
                {
                    var order = await ordering.GetCustomerAsync(customer.Value, id, token);
                    if (order.IsFailure)
                    {
                        return Result<PaymentInitiation>.Failure(order.Error);
                    }

                    if (order.Value.Status == OrderStatus.Cancelled)
                    {
                        return Result<PaymentInitiation>.Failure(
                            "ordering.order-cancelled",
                            "已取消的訂單不能付款。");
                    }

                    var returnUrl = BuildEcpayReturnUrl(configuration, context.Request);
                    return await payments.InitiateAsync(
                        new PaymentInitiationRequest(
                            id,
                            order.Value.GoodsTotal,
                            order.Value.ShippingFee,
                            $"GreyGray {order.Value.OrderNumber}",
                            returnUrl,
                            clientBackUrl),
                        token);
                },
                StatusCodes.Status200OK,
                cancellationToken);
        });

        // BE-35／必做 4：這是第 34 個冪等呼叫點，它「沒有」走 BffHttp.ExecuteIdempotentAsync，
        // 而是手寫了一模一樣的「IsFailure → AbandonAsync」／「catch → AbandonAsync + throw」形狀
        // （key = MerchantTradeNo、scope = webhook:ecpay），所以 grep ExecuteIdempotentAsync 抓不到它。
        // 形狀雖然相同，但這裡「維持現狀是對的」，不需要改用新的兩階段多載：
        //   ① HandleEcpayCallbackAsync 自己就是冪等的——payment.Status 已經是 Captured／
        //      PartiallyRefunded／Refunded 且 ProviderTransactionId 相同時直接回 Result.Success()，
        //      abandon 之後綠界重送同一筆回呼會被正確地再處理一次，不會產生第二份副作用；
        //   ② 成功時的回應是固定字串 "1|OK"，沒有「讀別的模組來組回應」那一段，
        //      也就沒有「副作用已 commit 之後才失敗」的區間（BE-34 分類：安全）。
        // 也就是說，這裡缺的是說明而不是修法。真要動它請看
        // BffHttp.ExecuteIdempotentAsync<TState, TResponse> 的 XML doc，四條語意寫在那裡。
        api.MapPost("/webhooks/ecpay", async (
            HttpContext context,
            IPaymentCommand payments,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            var fields = form.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
            if (!fields.TryGetValue("MerchantTradeNo", out var merchantTradeNo) ||
                string.IsNullOrWhiteSpace(merchantTradeNo))
            {
                return BffHttp.Problem(new Error("payment.invalid-callback", "綠界回呼缺少交易編號。"));
            }

            var canonical = string.Join(
                "&",
                fields.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}"));
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            var scope = "webhook:ecpay";
            var key = merchantTradeNo.Trim();
            var (outcome, _) = await idempotency.TryBeginAsync(key, scope, hash, cancellationToken);
            if (outcome == IdempotencyOutcome.AlreadyCompleted)
            {
                return Results.Text("1|OK", "text/plain", Encoding.UTF8);
            }

            if (outcome == IdempotencyOutcome.InFlight)
            {
                return Results.StatusCode(StatusCodes.Status409Conflict);
            }

            if (outcome == IdempotencyOutcome.KeyReusedWithDifferentPayload)
            {
                return BffHttp.Problem(
                    new Error("payment.callback-payload-mismatch", "同一交易編號的回呼內容不一致。"));
            }

            try
            {
                var result = await payments.HandleEcpayCallbackAsync(fields, cancellationToken);
                if (result.IsFailure)
                {
                    await idempotency.AbandonAsync(key, scope, cancellationToken);
                    return BffHttp.Problem(result.Error);
                }

                await idempotency.CompleteAsync(key, scope, "1|OK", cancellationToken);
                return Results.Text("1|OK", "text/plain", Encoding.UTF8);
            }
            catch
            {
                await idempotency.AbandonAsync(key, scope, CancellationToken.None);
                throw;
            }
        });
    }

    /// <summary>
    /// 端點層的 logger。<see cref="M1aEndpoints"/> 是 static class，不能當
    /// <c>ILogger&lt;T&gt;</c> 的型別引數，所以用固定的類別名稱字串建；
    /// 在 Map 階段建一次就好，不必每個請求重建。
    /// </summary>
    private static ILogger LoggerFor(IEndpointRouteBuilder endpoints) =>
        endpoints.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GreyGray.Api.Storefront.M1aEndpoints");

    private static async Task<CustomerId?> GetCustomerAsync(
        HttpContext context,
        ISessionStore sessions,
        CancellationToken cancellationToken)
    {
        var session = await BffHttp.GetSessionAsync(
            context,
            sessions,
            SessionCookie,
            SessionSubjectKind.Customer,
            cancellationToken);
        return session is not null && Guid.TryParseExact(session.SubjectId, "N", out var id)
            ? new CustomerId(id)
            : null;
    }

    internal static CartId GetOrCreateCartId(HttpContext context)
    {
        if (TryGetCartId(context, out var cartId))
        {
            return cartId;
        }

        cartId = CartId.New();
        SetCartCookie(context.Response, cartId);
        return cartId;
    }

    internal static bool TryGetCartId(HttpContext context, out CartId cartId)
    {
        if (context.Request.Cookies.TryGetValue(CartCookie, out var raw)
            && Guid.TryParseExact(raw, "N", out var parsed))
        {
            cartId = new CartId(parsed);
            return true;
        }

        cartId = default;
        return false;
    }

    /// <summary>
    /// <c>POST /v1/cart/lines</c>。BE-41 從 <see cref="MapCart"/> 的 inline lambda 抽出來，
    /// 好讓測試直接呼叫。
    /// </summary>
    internal static async Task<IResult> AddCartLineAsync(
        AddCartLineInput input,
        HttpContext context,
        ISessionStore sessions,
        ICheckoutApplication checkout,
        IIdempotencyStore idempotency,
        CancellationToken cancellationToken)
    {
        var cartId = GetOrCreateCartId(context);
        var customer = await GetCustomerAsync(context, sessions, cancellationToken);
        var request = new AddCartLineRequest(
            cartId,
            customer,
            input.SkuId,
            input.Mode,
            input.CampaignOfferId,
            input.Quantity);
        return await BffHttp.ExecuteIdempotentAsync(
            context,
            idempotency,
            "storefront:cart:lines:add",
            request,
            async token =>
            {
                var added = await checkout.AddLineAsync(request, token);

                // #36：cookie 指向的車已結案（下過單）或已經不是這位訪客的了
                // （登入時建的車，登出後 IsAccessibleBy 為 false → not-found）。
                // 兩種都是「這顆 cookie 該退休了」，換一顆新車重試一次。
                if (added.IsFailure &&
                    added.Error.Code is "checkout.cart-already-completed" or "checkout.cart-not-found")
                {
                    cartId = CartId.New();
                    SetCartCookie(context.Response, cartId);
                    added = await checkout.AddLineAsync(request with { CartId = cartId }, token);
                }

                return added.IsSuccess
                    ? Result<CartResponse>.Success(ToCart(added.Value))
                    : Result<CartResponse>.Failure(added.Error);
            },
            StatusCodes.Status200OK,
            cancellationToken);
    }

    /// <summary>
    /// <c>POST /v1/auth/logout</c>。BE-41 從 <see cref="MapAuth"/> 的 inline lambda 抽出來，
    /// 好讓測試直接呼叫（#36 要驗的是 <c>Set-Cookie</c>，那是 Host 層的事）。
    /// </summary>
    internal static async Task<IResult> LogoutAsync(
        HttpContext context,
        ISessionStore sessions,
        IIdempotencyStore idempotency,
        CancellationToken cancellationToken)
    {
        var customer = await GetCustomerAsync(context, sessions, cancellationToken);
        if (customer is null)
        {
            return BffHttp.Unauthorized();
        }

        return await BffHttp.ExecuteIdempotentAsync(
            context,
            idempotency,
            $"storefront:{customer}:auth:logout",
            request: null,
            async token =>
            {
                await BffHttp.DeleteSessionAsync(context, sessions, SessionCookie, token);

                // #36：購物車是綁在會員身上的（Cart.IsAccessibleBy），留著這顆 cookie
                // 只會讓登出後的訪客每次加入商品都拿到「找不到購物車」。
                ClearCartCookie(context.Response);
                return Result.Success();
            },
            cancellationToken);
    }

    /// <summary>
    /// <c>GET /v1/cart</c>。BE-41 從 <see cref="MapCart"/> 的 inline lambda 抽出來，
    /// 比照 <see cref="CompleteCheckoutAsync"/>，好讓測試直接呼叫。
    /// </summary>
    /// <remarks>
    /// #36：<c>gg_cart</c> 指向的車存在、但不屬於現在這位使用者時，Checkout 回
    /// <c>checkout.cart-not-found</c>（服務層的語意刻意不改——<c>/cart/quote</c>、
    /// <c>/cart/checkout</c> 仍要回 404）。但「看購物車」不該因此變成 404：
    /// 換一顆新 cookie、回空購物車，客人就能重新開始。只重試一次。
    /// </remarks>
    internal static async Task<IResult> GetCartAsync(
        HttpContext context,
        ISessionStore sessions,
        ICheckoutApplication checkout,
        CancellationToken cancellationToken)
    {
        var hadCartCookie = context.Request.Cookies.ContainsKey(CartCookie);
        var cartId = GetOrCreateCartId(context);
        var customer = await GetCustomerAsync(context, sessions, cancellationToken);
        var result = await checkout.GetCartAsync(cartId, customer, cancellationToken);

        // #44：這是第二道破口。只修 checkout 那條路的話，購物車頁與徽章還是會顯示上一張單的
        // 東西——GetCartAsync 對已結案的車照樣回 200，車裡的品項一件不少。比照下面
        // cart-not-found 的做法：換一顆新 cookie、回空車，客人就能重新開始。
        // （服務層刻意不把「已結案」變成失敗——/cart/quote 與 /cart/checkout 的語意不動。）
        if (result.IsSuccess && result.Value.IsCompleted)
        {
            cartId = CartId.New();
            SetCartCookie(context.Response, cartId);
            result = await checkout.GetCartAsync(cartId, customer, cancellationToken);
        }
        else if (hadCartCookie && result.IsFailure && result.Error.Code == "checkout.cart-not-found")
        {
            cartId = CartId.New();
            SetCartCookie(context.Response, cartId);
            result = await checkout.GetCartAsync(cartId, customer, cancellationToken);
        }

        return result.IsSuccess ? Results.Ok(ToCart(result.Value)) : BffHttp.Problem(result.Error);
    }

    /// <summary>
    /// 刪 <c>gg_cart</c>。選項要跟 <see cref="SetCartCookie"/> 一致，
    /// 否則瀏覽器不會認為是同一顆 cookie，刪不掉（比照 <c>BffHttp.ClearSessionCookie</c>）。
    /// </summary>
    private static void ClearCartCookie(HttpResponse response) =>
        response.Cookies.Delete(CartCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });

    private static void SetCartCookie(HttpResponse response, CartId cartId) =>
        response.Cookies.Append(CartCookie, cartId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromDays(30),
            IsEssential = true,
        });

    private static async Task CreateSessionAsync(
        HttpContext context,
        ISessionStore sessions,
        CustomerMe me,
        CancellationToken cancellationToken)
    {
        var token = await sessions.CreateAsync(
            me.Id.ToString(),
            SessionSubjectKind.Customer,
            TenantId.Default,
            role: null,
            cancellationToken);
        BffHttp.SetSessionCookie(context.Response, SessionCookie, token);
    }

    /// <summary>
    /// 組出綠界 <c>ClientBackURL</c>（完成頁「返回商店」按鈕）要導回的前台網址：
    /// <c>{Storefront:PublicOrigin}/payment/result?orderId=…</c>。
    /// </summary>
    /// <remarks>
    /// <b>缺設定就在這裡炸，而且刻意不給 localhost 預設值。</b>
    /// cookie 是依 hostname 隔離的——dev 的前台跑在 <c>127.0.0.1</c>，猜成 <c>localhost</c>
    /// 會讓客人導回一個沒有 session 的網域，<c>/payment/result</c> 直接拿 401，
    /// 而且症狀看起來像「登入壞了」。跟 Payment 設定「清楚地說缺設定」是同一種 fail-fast。
    /// 驗證放在付款端點而不是 Program.cs 啟動時：ops/check-openapi.ps1 與測試都會起這個 Host，
    /// 啟動時強制驗會讓它們全部起不來。
    /// </remarks>
    private static Uri BuildPaymentResultUrl(IConfiguration configuration, Guid orderId)
        => StorefrontUrls.BuildPublicUrl(
            configuration,
            $"/payment/result?orderId={orderId:N}",
            "綠界完成頁的「返回商店」按鈕要用它組出 /payment/result?orderId=…，"
            + "沒有它客人付完款就回不了商店。dev 請設成前台實際的 http://127.0.0.1:<port>。");

    /// <summary>
    /// 組出綠界 <c>ReturnURL</c>（綠界伺服器對伺服器打回來的付款結果回呼）：
    /// <c>{Storefront:PublicApiOrigin}/v1/webhooks/ecpay</c>；沒設這個鍵就退回用
    /// 這一次請求的 scheme／host（dev 的行為，跟 BE-42 之前完全一樣）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 為什麼要能由設定指定：正式機在 Cloudflare Tunnel 後面，cloudflared 打的是
    /// <c>http://127.0.0.1:5000</c>，所以 <see cref="HttpRequest.Host"/> 收到的是本機位址，
    /// 組出來的 <c>ReturnURL</c> 綠界從外面根本打不到（綠界要求 ReturnURL 對外可達且只准 80／443）。
    /// </para>
    /// <para>
    /// 為什麼<b>不</b>做成必填（跟 <c>BuildPaymentResultUrl</c> 不同）：dev 的綠界模擬器
    /// 跟 Host 同一台，request-based 的 <c>http://127.0.0.1:5000/v1/webhooks/ecpay</c> 本來就是對的，
    /// 逼 dev 多設一個鍵沒有換到任何安全性。反過來說，設了但格式錯就一定要炸——
    /// 悄悄退回 request-based 的話，正式機會變成「綠界收到一個打不通的網址」，
    /// 症狀是付款永遠停在待付款，而且沒有任何錯誤訊息。
    /// </para>
    /// </remarks>
    internal static Uri BuildEcpayReturnUrl(IConfiguration configuration, HttpRequest request)
        => StorefrontUrls.BuildPublicApiUrl(
            configuration,
            request,
            "/v1/webhooks/ecpay",
            "綠界的 ReturnURL 要用它組出 /v1/webhooks/ecpay。留空則退回使用這一次請求的 scheme/host。");

    private static bool TryId(string raw, out Guid id) => Guid.TryParseExact(raw, "N", out id);

    private static CustomerMe ToMe(CustomerProfile profile) => new(
        profile.Id,
        profile.DisplayName,
        profile.Tier,
        profile.IsActive,
        profile.Email,
        profile.PhoneNumberMasked,
        profile.LineLinked);

    private static CartResponse ToCart(CartView cart) => new(
        cart.Id,
        cart.Lines.Select(line => new CartLineResponse(
            line.Id,
            line.SkuId,
            line.ProductId,
            line.Name,
            line.VariantName,
            line.ImageUrl,
            line.Mode,
            line.CampaignId,
            line.CampaignOffer,
            line.Quantity,
            line.UnitPrice,
            line.LineTotal,
            line.AvailabilityWarning)).ToArray(),
        cart.GoodsTotal,
        cart.HasMixedModes,
        cart.Quote is null
            ? null
            : new QuoteResponse(
                cart.Quote.DeliveryMethod,
                cart.GoodsTotal,
                cart.Quote.ShippingFee,
                cart.GrandTotal ?? cart.GoodsTotal.Add(cart.Quote.ShippingFee),
                cart.Quote.ActualWeightGram,
                cart.Quote.VolumetricWeightGram,
                cart.Quote.BillableWeightGram,
                cart.Quote.AppliedStrategy,
                cart.Quote.Explain));

    private static QuoteResponse ToQuote(CheckoutQuote quote) => new(
        quote.Snapshot.DeliveryMethod,
        quote.GoodsTotal,
        quote.Snapshot.ShippingFee,
        quote.GrandTotal,
        quote.Snapshot.ActualWeightGram,
        quote.Snapshot.VolumetricWeightGram,
        quote.Snapshot.BillableWeightGram,
        quote.Snapshot.AppliedStrategy,
        quote.Snapshot.Explain);

    private static OrderListItemResponse ToOrderListItem(OrderView order) =>
        new(
            order.Id,
            order.OrderNumber,
            order.Status,
            order.GrandTotal,
            order.PlacedAt,
            order.Lines.Count,
            null);

    /// <summary>把 <see cref="OrderView"/> 組成前台 <c>Order</c> 回應。</summary>
    /// <remarks>
    /// <para>
    /// <b>BE-35：這個方法不會失敗。</b>呼叫它的時候訂單已經 commit 了，讓它回 <c>Result</c>
    /// 正是「現在卡在哪 #22」那一整個家族的來源——組回應讀不到別的模組，不該讓已經成立的
    /// 下單／取消看起來像沒發生。
    /// </para>
    /// <para>
    /// 讀不到 SKU 或地址時填退化值，兩條硬性要求：<b>① 不改契約</b>——退化回應仍符合
    /// <c>docs/05-API契約.md</c>／<c>docs/api/openapi.storefront.yaml</c> 的 <c>Order</c>
    /// schema，必填欄位一律有值（<c>name</c> 用 <c>skuId</c> 的字串形式、<c>productId</c>
    /// 用全零 ID，兩者都是 32 字元十六進位，符合 <c>Id</c> 的 pattern）；
    /// <b>② 不准靜默</b>——每一次退化都 <c>LogError</c> 留痕。
    /// </para>
    /// </remarks>
    private static async Task<OrderResponse> ToOrderAsync(
        OrderView order,
        ICatalogQuery catalog,
        ICustomerDirectory customers,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var lines = new List<OrderLineResponse>(order.Lines.Count);
        foreach (var line in order.Lines)
        {
            var sku = await catalog.GetSkuAsync(line.SkuId, cancellationToken);
            if (sku.IsFailure)
            {
                logger.LogError(
                    "訂單 {OrderId} 的品項 {OrderLineId} 讀不到 SKU {SkuId}（{ErrorCode}），" +
                    "改以退化值組回應；訂單本身已經成立。",
                    order.Id,
                    line.Id,
                    line.SkuId,
                    sku.Error.Code);
            }

            lines.Add(new OrderLineResponse(
                line.Id,
                line.SkuId,
                sku.IsSuccess ? sku.Value.ProductId : default,
                sku.IsSuccess ? sku.Value.Name : line.SkuId.ToString(),
                sku.IsSuccess ? sku.Value.VariantName : null,
                null,
                line.Mode,
                line.Status,
                line.Quantity,
                line.UnitPrice,
                line.LineTotal,
                line.CampaignId,
                line.RefundedAmount));
        }

        ShippingAddressResponse? address = null;
        if (order.ShippingAddressId is not null)
        {
            var found = await customers.GetAddressAsync(order.ShippingAddressId.Value, cancellationToken);
            if (found.IsSuccess)
            {
                address = new ShippingAddressResponse(
                    found.Value.Id,
                    found.Value.RecipientName,
                    found.Value.PhoneNumber,
                    found.Value.PostalCode,
                    found.Value.City,
                    found.Value.District,
                    found.Value.StreetAddress,
                    false);
            }
            else
            {
                // shippingAddress 在契約裡本來就可為 null，所以行為沒變；BE-35 只是不再讓它靜默。
                logger.LogError(
                    "訂單 {OrderId} 讀不到收件地址 {AddressId}（{ErrorCode}），回應的 shippingAddress 留空。",
                    order.Id,
                    order.ShippingAddressId.Value,
                    found.Error.Code);
            }
        }

        return new OrderResponse(
            order.Id,
            order.OrderNumber,
            order.Status,
            order.ShippingPolicy,
            order.DeliveryMethod,
            order.GoodsTotal,
            order.ShippingFee,
            order.GrandTotal,
            order.PaidAmount,
            lines,
            address,
            order.ConvenienceStoreName ?? order.ConvenienceStoreCode,
            order.ConvenienceStoreAddress,
            // ★ ADR-039：收件人一律取<b>訂單快照</b>。上面那段即時回查地址簿的 address
            //   是給 shippingAddress 其他欄位用的，客人改過地址之後它已經不是下單當時的事實（#56）。
            order.RecipientName,
            order.RecipientPhone,
            order.PlacedAt,
            order.PaymentDueAt,
            order.QuoteExplain);
    }

    /// <param name="campaign">
    /// 這個商品目前掛著的、仍在收單的團。現貨一律是 <c>null</c>；預購商品沒有任何開著的團時
    /// 也是 <c>null</c>，此時 <c>campaignId</c> 與 <c>priceFrom</c> 都留空，前端會保守地
    /// 視為不可下單——那是對的，因為它確實買不到。
    /// </param>
    private static ProductListItemResponse ToProductListItem(
        StorefrontProductListItem item,
        StorefrontProductCampaign? campaign,
        bool isFavorited) => new(
        item.Id,
        item.Name,
        item.ShortDescription,
        item.ImageUrl,
        // 契約：priceFrom 現貨是標價，預購是該團的定價（多 SKU 取最低）。
        campaign?.PriceFrom ?? item.PriceFrom,
        item.UnitPriceLabel,
        [],
        isFavorited,
        item.Mode,
        campaign?.Campaign.Id);

    /// <param name="campaign">同 <see cref="ToProductListItem"/>。</param>
    private static async Task<ProductDetailResponse> ToProductDetailAsync(
        StorefrontProductDetail product,
        IInventoryQuery inventory,
        StorefrontProductCampaign? campaign,
        bool isFavorited,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<SkuId, int> availability = new Dictionary<SkuId, int>();
        if (product.Mode == FulfillmentMode.Stock)
        {
            var result = await inventory.GetAvailabilityAsync(
                product.Skus.Select(sku => sku.Id).ToArray(),
                cancellationToken);
            if (result.IsSuccess)
            {
                availability = result.Value.ToDictionary(value => value.SkuId, value => value.Available);
            }
        }

        return new ProductDetailResponse(
            product.Id,
            product.Name,
            product.Description,
            product.ShortDescription,
            product.Images,
            product.CategoryId,
            product.Mode,
            campaign?.Campaign,
            product.Skus.Select(sku =>
            {
                var offer = campaign?.Offers.GetValueOrDefault(sku.Id);
                return new SkuResponse(
                    sku.Id,
                    sku.Name,
                    sku.VariantName,
                    sku.WeightGram,
                    sku.Size,
                    sku.UnitOfMeasure,
                    sku.UnitCount,
                    sku.IsActive,
                    // 契約明文：預購 SKU 的 available 恆為 0，但仍然可以下單，
                    // 前端不要拿這個值擋預購。所以只有現貨才去查庫存。
                    availability.GetValueOrDefault(sku.Id),
                    // 契約：price 現貨是標價，預購是該團的定價。
                    product.Mode == FulfillmentMode.Stock ? sku.ListPrice : offer?.SellingPrice,
                    offer?.OfferId);
            }).ToArray(),
            isFavorited);
    }

    private static CampaignDetailResponse ToCampaignDetail(StorefrontCampaignDetail detail) => new(
        detail.Campaign.Id,
        detail.Campaign.Title,
        detail.Campaign.Destination,
        detail.Campaign.DepartAt,
        detail.Campaign.ReturnAt,
        detail.Campaign.ClosesAt,
        detail.Campaign.Status,
        detail.Campaign.IsAcceptingOrders,
        detail.Campaign.CoverImageUrl,
        detail.Description,
        detail.Offers);

    private sealed record CustomerMe(
        CustomerId Id,
        string DisplayName,
        MemberTier Tier,
        bool IsActive,
        string? Email,
        string PhoneNumberMasked,
        bool LineLinked);

    private sealed record StoredValueResponse(Money Balance);

    private sealed record CategoryResponse(CategoryId Id, string Name, string? ImageUrl);

    internal sealed record ProductPageResponse(
        IReadOnlyList<ProductListItemResponse> Items,
        string? NextCursor);

    internal sealed record ProductListItemResponse(
        ProductId Id,
        string Name,
        string? ShortDescription,
        string? ImageUrl,
        Money? PriceFrom,
        string? UnitPriceLabel,
        IReadOnlyList<string> Badges,
        bool IsFavorited,
        FulfillmentMode Mode,
        CampaignId? CampaignId);

    internal sealed record SkuResponse(
        SkuId Id,
        string Name,
        string? VariantName,
        int WeightGram,
        Dimensions Size,
        string? UnitOfMeasure,
        int? UnitCount,
        bool IsActive,
        int Available,
        Money? Price,
        CampaignOfferId? CampaignOfferId);

    internal sealed record ProductDetailResponse(
        ProductId Id,
        string Name,
        string? Description,
        string? ShortDescription,
        IReadOnlyList<string> Images,
        CategoryId? CategoryId,
        FulfillmentMode Mode,
        StorefrontCampaignListItem? Campaign,
        IReadOnlyList<SkuResponse> Skus,
        bool IsFavorited);

    private sealed record CampaignDetailResponse(
        CampaignId Id,
        string Title,
        string Destination,
        DateOnly DepartAt,
        DateOnly ReturnAt,
        DateTimeOffset ClosesAt,
        CampaignStatus Status,
        bool IsAcceptingOrders,
        string? CoverImageUrl,
        string? Description,
        IReadOnlyList<StorefrontCampaignOffer> Offers);

    internal sealed record AddCartLineInput(
        SkuId SkuId,
        FulfillmentMode Mode,
        CampaignOfferId? CampaignOfferId,
        int Quantity);

    private sealed record UpdateCartLineInput(int Quantity);

    private sealed record QuoteCartInput(DeliveryMethod DeliveryMethod);

    /// <param name="ShippingPolicy">
    /// ADR-030：可為 null。<c>Cart.hasMixedModes</c> 為 true 時才必填（缺了 Checkout 回
    /// <c>checkout.shipping-policy-required</c> → 422）；單一模式忽略客人送的值，由後端推導。
    /// <b>不可以改回不可為 null 的 enum</b>——那會讓 JSON <c>null</c> 在綁定期就丟
    /// <c>JsonException</c>，客人連 401 都拿不到（#37）。
    /// </param>
    internal sealed record CompleteCheckoutInput(
        DeliveryMethod DeliveryMethod,
        ShippingPolicy? ShippingPolicy,
        AddressId? ShippingAddressId,
        string? ConvenienceStoreSelectionId,
        string? ConvenienceStoreCode,
        string? RecipientName,
        string? RecipientPhone,
        string? BuyerNote);

    /// <remarks>
    /// ADR-039：<c>RecipientName</c>／<c>RecipientPhone</c> <b>一定要在指紋裡</b>。
    /// 它們是會被凍結進訂單的事實，漏掉的話同一把 <c>Idempotency-Key</c> 換掉收件人會直接
    /// 回快取的舊回應、把新收件人靜靜吞掉。加上去之後那種情況改回
    /// <c>422 platform.idempotency-key-reused</c>——<b>這個行為改變是要的</b>。
    /// </remarks>
    private sealed record CompleteCheckoutFingerprint(
        CartId CartId,
        CustomerId CustomerId,
        DeliveryMethod DeliveryMethod,
        ShippingPolicy? ShippingPolicy,
        AddressId? ShippingAddressId,
        string? ConvenienceStoreSelectionId,
        string? ConvenienceStoreCode,
        string? RecipientName,
        string? RecipientPhone,
        string? BuyerNote);

    internal sealed record CancelOrderInput(string? Reason);

    private sealed record CancelCustomerOrderRequest(
        CustomerId CustomerId,
        OrderId OrderId,
        string? Reason);

    private sealed record CartLineResponse(
        CartLineId Id,
        SkuId SkuId,
        ProductId ProductId,
        string Name,
        string? VariantName,
        string? ImageUrl,
        FulfillmentMode Mode,
        CampaignId? CampaignId,
        CampaignOfferId? CampaignOfferId,
        int Quantity,
        Money UnitPrice,
        Money LineTotal,
        string? AvailabilityWarning);

    private sealed record CartResponse(
        CartId Id,
        IReadOnlyList<CartLineResponse> Lines,
        Money GoodsTotal,
        bool HasMixedModes,
        QuoteResponse? Quote);

    private sealed record QuoteResponse(
        DeliveryMethod DeliveryMethod,
        Money GoodsTotal,
        Money ShippingFee,
        Money GrandTotal,
        int ActualWeightGram,
        int VolumetricWeightGram,
        int BillableWeightGram,
        ShippingStrategyKind AppliedStrategy,
        IReadOnlyList<string> Explain);

    private sealed record OrderListItemResponse(
        OrderId Id,
        string OrderNumber,
        OrderStatus Status,
        Money GrandTotal,
        DateTimeOffset PlacedAt,
        int LineCount,
        string? ThumbnailUrl);

    private sealed record OrderPageResponse(
        IReadOnlyList<OrderListItemResponse> Items,
        string? NextCursor);

    private sealed record ShippingAddressResponse(
        AddressId Id,
        string RecipientName,
        string PhoneNumber,
        string PostalCode,
        string City,
        string District,
        string StreetAddress,
        bool IsDefault);

    private sealed record OrderLineResponse(
        OrderLineId Id,
        SkuId SkuId,
        ProductId ProductId,
        string Name,
        string? VariantName,
        string? ImageUrl,
        FulfillmentMode Mode,
        OrderLineStatus Status,
        int Quantity,
        Money UnitPrice,
        Money LineTotal,
        CampaignId? CampaignId,
        Money? RefundedAmount);

    private sealed record OrderResponse(
        OrderId Id,
        string OrderNumber,
        OrderStatus Status,
        ShippingPolicy ShippingPolicy,
        DeliveryMethod DeliveryMethod,
        Money GoodsTotal,
        Money ShippingFee,
        Money GrandTotal,
        Money? PaidAmount,
        IReadOnlyList<OrderLineResponse> Lines,
        ShippingAddressResponse? ShippingAddress,
        string? ConvenienceStoreName,
        string? ConvenienceStoreAddress,
        string? RecipientName,
        string? RecipientPhone,
        DateTimeOffset PlacedAt,
        DateTimeOffset? PaymentDueAt,
        IReadOnlyList<string> QuoteExplain);
}
