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

namespace GreyGray.Api.Admin;

internal static class M1aEndpoints
{
    private const string SessionCookie = "gg_admin_session";
    private const string StaffItem = "greygray.staff-id";

    public static IEndpointRouteBuilder MapM1aAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/v1");
        MapAuth(api);
        MapCatalog(api);
        MapCampaign(api);
        MapOrders(api);
        MapLedger(api);
        return endpoints;
    }

    private static void MapAuth(RouteGroupBuilder api)
    {
        api.MapPost("/auth/login", async (
            StaffLoginInput input,
            HttpContext context,
            IStaffAccounts accounts,
            ISessionStore sessions,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                "admin:auth:login",
                input,
                async token =>
                {
                    var authenticated = await accounts.AuthenticateAsync(input, token);
                    if (authenticated.IsFailure)
                    {
                        return Result<StaffProfile>.Failure(authenticated.Error);
                    }

                    await CreateSessionAsync(context, sessions, authenticated.Value, token);
                    return authenticated.Value;
                },
                StatusCodes.Status200OK,
                cancellationToken,
                (staff, token) => CreateSessionAsync(context, sessions, staff, token)));

        api.MapPost("/auth/logout", async (
            HttpContext context,
            ISessionStore sessions,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                $"admin:{GetStaffId(context)}:auth:logout",
                request: null,
                async token =>
                {
                    await BffHttp.DeleteSessionAsync(context, sessions, SessionCookie, token);
                    return Result.Success();
                },
                cancellationToken))
            .AddEndpointFilter(new StaffRoleFilter(StaffRole.ReadOnly));

        api.MapGet("/me", async (
            HttpContext context,
            IStaffAccounts accounts,
            CancellationToken cancellationToken) =>
        {
            var result = await accounts.GetProfileAsync(GetStaffId(context), cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : BffHttp.Problem(result.Error);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.ReadOnly));
    }

    private static void MapOrders(RouteGroupBuilder api)
    {
        var logger = LoggerFor(api);

        api.MapGet("/orders", async (
            string? q,
            OrderStatus? status,
            string? campaignId,
            string? cursor,
            int? limit,
            IOrderingApplication ordering,
            ICustomerDirectory customers,
            CancellationToken cancellationToken) =>
        {
            CampaignId? campaign = null;
            if (campaignId is not null)
            {
                if (!TryId(campaignId, out var parsedCampaign))
                {
                    return BffHttp.Problem(new Error(
                        "ordering.invalid-campaign-id",
                        "開團識別格式錯誤。"));
                }

                campaign = new CampaignId(parsedCampaign);
            }

            OrderId? pageCursor = null;
            if (cursor is not null)
            {
                if (!TryId(cursor, out var parsedCursor))
                {
                    return BffHttp.Problem(new Error(
                        "ordering.invalid-cursor",
                        "分頁游標格式錯誤。"));
                }

                pageCursor = new OrderId(parsedCursor);
            }

            var result = await ordering.ListAdminAsync(
                new AdminOrderListRequest(q, status, campaign, pageCursor, limit ?? 20),
                cancellationToken);
            if (result.IsFailure)
            {
                return BffHttp.Problem(result.Error);
            }

            var projected = await ToAdminOrderListAsync(
                result.Value.Items,
                customers,
                cancellationToken);
            return projected.IsSuccess
                ? Results.Ok(new AdminOrderPage(projected.Value, result.Value.NextCursor))
                : BffHttp.Problem(projected.Error);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.ReadOnly));

        api.MapGet("/orders/{orderId}", async (
            string orderId,
            IOrderingApplication ordering,
            ICustomerDirectory customers,
            IPaymentQuery payments,
            ICatalogQuery catalog,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(orderId, out var parsed))
            {
                return BffHttp.Problem(new Error("ordering.order-not-found", "找不到指定的訂單。"));
            }

            var result = await ordering.GetAdminAsync(new OrderId(parsed), cancellationToken);
            if (result.IsFailure)
            {
                return BffHttp.Problem(result.Error);
            }

            // BE-35：ToAdminOrderAsync 不再會失敗，讀不到 customer／SKU／payment 時
            // 回退化值並留下 log。
            return Results.Ok(await ToAdminOrderAsync(
                result.Value,
                customers,
                payments,
                catalog,
                logger,
                cancellationToken));
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.ReadOnly));

        api.MapPost("/orders/{orderId}/cancel", async (
            string orderId,
            CancelOrderInput input,
            HttpContext context,
            IOrderingApplication ordering,
            ICustomerDirectory customers,
            IPaymentQuery payments,
            ICatalogQuery catalog,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await CancelOrderAsync(
                orderId,
                input,
                context,
                ordering,
                customers,
                payments,
                catalog,
                idempotency,
                logger,
                cancellationToken))
            .AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));

        api.MapPost("/orders/{orderId}/lines/{lineId}/cancel", async (
            string orderId,
            string lineId,
            CancelOrderInput input,
            HttpContext context,
            IOrderingApplication ordering,
            ICustomerDirectory customers,
            IPaymentQuery payments,
            ICatalogQuery catalog,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await CancelOrderLineAsync(
                orderId,
                lineId,
                input,
                context,
                ordering,
                customers,
                payments,
                catalog,
                idempotency,
                logger,
                cancellationToken))
            .AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));
    }

    /// <summary>
    /// <c>POST /v1/orders/{orderId}/cancel</c>（Admin）的處理邏輯（BE-34 分類表的 A3）。
    /// BE-35 從 inline lambda 抽出來（純搬移），好讓測試直接呼叫。
    /// </summary>
    /// <remarks>
    /// <c>CancelAdminAsync</c> commit 之後 <c>RefundRequested</c> 就已經送出去了，
    /// 而底層是狀態機守衛（訂單已 <c>Cancelled</c> 就擋下），所以「組回應失敗 → abandon → 重試」
    /// <b>回不了成功</b>。改用 <see cref="BffHttp.ExecuteIdempotentAsync{TState, TResponse}"/>
    /// 兩階段多載，<see cref="ToAdminOrderAsync"/> 落在不會失敗的 <c>render</c> 那一段。
    /// </remarks>
    internal static async Task<IResult> CancelOrderAsync(
        string orderId,
        CancelOrderInput input,
        HttpContext context,
        IOrderingApplication ordering,
        ICustomerDirectory customers,
        IPaymentQuery payments,
        ICatalogQuery catalog,
        IIdempotencyStore idempotency,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!TryId(orderId, out var parsed))
        {
            return BffHttp.Problem(new Error("ordering.order-not-found", "找不到指定的訂單。"));
        }

        return await BffHttp.ExecuteIdempotentAsync(
            context,
            idempotency,
            Scope(context, $"orders:{orderId}:cancel"),
            input,
            async token =>
            {
                var existing = await ordering.GetAdminAsync(new OrderId(parsed), token);
                if (existing.IsFailure)
                {
                    return Result<OrderView>.Failure(existing.Error);
                }

                return await ordering.CancelAdminAsync(
                    new OrderId(parsed),
                    input.Reason,
                    input.RefundTo,
                    token);
            },
            (order, token) => ToAdminOrderAsync(order, customers, payments, catalog, logger, token),
            StatusCodes.Status200OK,
            cancellationToken);
    }

    /// <summary>
    /// <c>POST /v1/orders/{orderId}/lines/{lineId}/cancel</c> 的處理邏輯
    /// （BE-34 分類表的 A4）。
    /// </summary>
    /// <remarks>
    /// 與 <see cref="CancelOrderAsync"/> 同款：<c>CancelLineAsync</c> commit 之後退款事件
    /// 已經送出，底層是品項狀態守衛（已 <c>Unavailable</c> 就擋下），重試回不了成功。
    /// BE-35 改用兩階段多載。
    /// </remarks>
    internal static async Task<IResult> CancelOrderLineAsync(
        string orderId,
        string lineId,
        CancelOrderInput input,
        HttpContext context,
        IOrderingApplication ordering,
        ICustomerDirectory customers,
        IPaymentQuery payments,
        ICatalogQuery catalog,
        IIdempotencyStore idempotency,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!TryId(orderId, out var parsedOrder))
        {
            return BffHttp.Problem(new Error("ordering.order-not-found", "找不到指定的訂單。"));
        }

        if (!TryId(lineId, out var parsedLine))
        {
            return BffHttp.Problem(new Error("ordering.order-line-not-found", "找不到指定的訂單品項。"));
        }

        return await BffHttp.ExecuteIdempotentAsync(
            context,
            idempotency,
            Scope(context, $"orders:{orderId}:lines:{lineId}:cancel"),
            input,
            async token =>
            {
                var existing = await ordering.GetAdminAsync(new OrderId(parsedOrder), token);
                if (existing.IsFailure)
                {
                    return Result<OrderView>.Failure(existing.Error);
                }

                return await ordering.CancelLineAsync(
                    new OrderId(parsedOrder),
                    new OrderLineId(parsedLine),
                    input.Reason,
                    input.RefundTo,
                    token);
            },
            (order, token) => ToAdminOrderAsync(order, customers, payments, catalog, logger, token),
            StatusCodes.Status200OK,
            cancellationToken);
    }

    private static void MapCatalog(RouteGroupBuilder api)
    {
        api.MapGet("/categories", async (
            ICatalogAdministration catalog,
            CancellationToken cancellationToken) =>
            Results.Ok(await catalog.ListCategoriesAsync(cancellationToken)))
            .AddEndpointFilter(new StaffRoleFilter(StaffRole.ReadOnly));

        api.MapPost("/categories", async (
            CategoryInput input,
            HttpContext context,
            ICatalogAdministration catalog,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                Scope(context, "categories:create"),
                input,
                token => catalog.CreateCategoryAsync(input, token),
                StatusCodes.Status201Created,
                cancellationToken))
            .AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));

        api.MapPatch("/categories/{categoryId}", async (
            string categoryId,
            CategoryInput input,
            HttpContext context,
            ICatalogAdministration catalog,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(categoryId, out var parsed))
            {
                return BffHttp.Problem(new Error("catalog.category-not-found", "找不到分類。"));
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                Scope(context, $"categories:{categoryId}:update"),
                input,
                token => catalog.UpdateCategoryAsync(new CategoryId(parsed), input, token),
                StatusCodes.Status200OK,
                cancellationToken);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));

        api.MapGet("/products", async (
            string? q,
            string? categoryId,
            bool? includeArchived,
            string? cursor,
            int? limit,
            ICatalogAdministration catalog,
            IInventoryQuery inventory,
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

            var result = await catalog.ListProductsAsync(
                new ProductSearch(q, category, null, includeArchived ?? false, cursor, limit ?? 20),
                cancellationToken);
            if (result.IsFailure)
            {
                return BffHttp.Problem(result.Error);
            }

            var mapped = await ToAdminProductsAsync(result.Value.Items, inventory, cancellationToken);
            return mapped.IsSuccess
                ? Results.Ok(new AdminProductPage(mapped.Value, result.Value.NextCursor))
                : BffHttp.Problem(mapped.Error);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.ReadOnly));

        api.MapPost("/products", async (
            AdminProductRequest input,
            HttpContext context,
            ICatalogAdministration catalog,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                Scope(context, "products:create"),
                input,
                async token =>
                {
                    var created = await catalog.CreateProductAsync(input.ToContract(), token);
                    return created.IsSuccess
                        ? Result<AdminProductResponse>.Success(ToAdminProduct(created.Value))
                        : Result<AdminProductResponse>.Failure(created.Error);
                },
                StatusCodes.Status201Created,
                cancellationToken))
            .AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));

        api.MapGet("/products/{productId}", async (
            string productId,
            ICatalogAdministration catalog,
            IInventoryQuery inventory,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(productId, out var parsed))
            {
                return BffHttp.Problem(new Error("catalog.product-not-found", "找不到商品。"));
            }

            var result = await catalog.GetProductAsync(new ProductId(parsed), cancellationToken);
            if (result.IsFailure)
            {
                return BffHttp.Problem(result.Error);
            }

            var mapped = await ToAdminProductsAsync([result.Value], inventory, cancellationToken);
            return mapped.IsSuccess ? Results.Ok(mapped.Value[0]) : BffHttp.Problem(mapped.Error);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.ReadOnly));

        api.MapPatch("/products/{productId}", async (
            string productId,
            AdminProductRequest input,
            HttpContext context,
            ICatalogAdministration catalog,
            IInventoryQuery inventory,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(productId, out var parsed))
            {
                return BffHttp.Problem(new Error("catalog.product-not-found", "找不到商品。"));
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                Scope(context, $"products:{productId}:update"),
                input,
                async token =>
                {
                    var updated = await catalog.UpdateProductAsync(
                        new ProductId(parsed),
                        input.ToContract(),
                        token);
                    if (updated.IsFailure)
                    {
                        return Result<AdminProductResponse>.Failure(updated.Error);
                    }

                    var mapped = await ToAdminProductsAsync([updated.Value], inventory, token);
                    return mapped.IsSuccess
                        ? Result<AdminProductResponse>.Success(mapped.Value[0])
                        : Result<AdminProductResponse>.Failure(mapped.Error);
                },
                StatusCodes.Status200OK,
                cancellationToken);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));

        api.MapPost("/products/{productId}/skus", async (
            string productId,
            AdminSkuRequest input,
            HttpContext context,
            ICatalogAdministration catalog,
            IInventoryQuery inventory,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await CreateSkuAsync(
                productId,
                input,
                context,
                catalog,
                inventory,
                idempotency,
                cancellationToken))
            .AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));

        api.MapPatch("/skus/{skuId}", async (
            string skuId,
            AdminSkuRequest input,
            HttpContext context,
            ICatalogAdministration catalog,
            IInventoryQuery inventory,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(skuId, out var parsed))
            {
                return BffHttp.Problem(new Error("catalog.sku-not-found", "找不到 SKU。"));
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                Scope(context, $"skus:{skuId}:update"),
                input,
                async token =>
                {
                    var updated = await catalog.UpdateSkuAsync(
                        new SkuId(parsed),
                        input.ToContract(),
                        token);
                    if (updated.IsFailure)
                    {
                        return Result<AdminSkuResponse>.Failure(updated.Error);
                    }

                    var availability = await inventory.GetAvailabilityAsync([updated.Value.Id], token);
                    return availability.IsSuccess
                        ? Result<AdminSkuResponse>.Success(ToAdminSku(
                            updated.Value,
                            availability.Value.Single().Available))
                        : Result<AdminSkuResponse>.Failure(availability.Error);
                },
                StatusCodes.Status200OK,
                cancellationToken);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));
    }

    /// <summary>
    /// <c>POST /v1/products/{productId}/skus</c>（Admin，ADR-032）的處理邏輯。
    /// 形狀比照 <c>PATCH /v1/skus/{skuId}</c>，只是改呼叫
    /// <see cref="ICatalogAdministration.CreateSkuAsync"/> 並回 <c>201</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 比照 <see cref="CancelOrderLineAsync"/> 抽成具名方法（純搬移），
    /// 好讓測試直接呼叫；inline lambda 在 repo 目前的測試基礎下碰不到。
    /// </para>
    /// <para>
    /// 新 SKU 一定沒有批號，<see cref="IInventoryQuery.GetAvailabilityAsync"/> 對
    /// 查不到 lot 的 SKU 回的是 <c>0</c> 而不是失敗（它把要查的 id 逐一補齊），
    /// 所以這裡不需要、也不准把失敗吞成 0。
    /// </para>
    /// </remarks>
    internal static async Task<IResult> CreateSkuAsync(
        string productId,
        AdminSkuRequest input,
        HttpContext context,
        ICatalogAdministration catalog,
        IInventoryQuery inventory,
        IIdempotencyStore idempotency,
        CancellationToken cancellationToken)
    {
        if (!TryId(productId, out var parsed))
        {
            return BffHttp.Problem(new Error("catalog.product-not-found", "找不到商品。"));
        }

        return await BffHttp.ExecuteIdempotentAsync(
            context,
            idempotency,
            Scope(context, $"products:{productId}:skus:create"),
            input,
            async token =>
            {
                var created = await catalog.CreateSkuAsync(
                    new ProductId(parsed),
                    input.ToContract(),
                    token);
                if (created.IsFailure)
                {
                    return Result<AdminSkuResponse>.Failure(created.Error);
                }

                var availability = await inventory.GetAvailabilityAsync([created.Value.Id], token);
                return availability.IsSuccess
                    ? Result<AdminSkuResponse>.Success(ToAdminSku(
                        created.Value,
                        availability.Value.Single().Available))
                    : Result<AdminSkuResponse>.Failure(availability.Error);
            },
            StatusCodes.Status201Created,
            cancellationToken);
    }

    private static void MapCampaign(RouteGroupBuilder api)
    {
        api.MapGet("/campaigns", async (
            CampaignStatus? status,
            string? cursor,
            int? limit,
            ICampaignAdministration campaigns,
            CancellationToken cancellationToken) =>
        {
            var result = await campaigns.ListAsync(
                new CampaignPageRequest(status, cursor, limit ?? 20),
                cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : BffHttp.Problem(result.Error);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.ReadOnly));

        api.MapPost("/campaigns", async (
            CampaignDraftInput input,
            HttpContext context,
            ICampaignAdministration campaigns,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                Scope(context, "campaigns:create"),
                input,
                token => campaigns.CreateDraftAsync(input, token),
                StatusCodes.Status201Created,
                cancellationToken))
            .AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));

        api.MapGet("/campaigns/{campaignId}", async (
            string campaignId,
            ICampaignAdministration campaigns,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(campaignId, out var parsed))
            {
                return BffHttp.Problem(new Error("campaign.not-found", "找不到指定的開團。"));
            }

            var result = await campaigns.GetDetailAsync(new CampaignId(parsed), cancellationToken);
            return result.IsSuccess
                ? Results.Ok(ToAdminCampaignDetail(result.Value))
                : BffHttp.Problem(result.Error);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.ReadOnly));

        api.MapPatch("/campaigns/{campaignId}", async (
            string campaignId,
            CampaignDraftInput input,
            HttpContext context,
            ICampaignAdministration campaigns,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await CampaignWrite(
                campaignId,
                context,
                idempotency,
                $"campaigns:{campaignId}:update",
                input,
                (id, token) => campaigns.UpdateDraftAsync(id, input, token),
                cancellationToken))
            .AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));

        api.MapPost("/campaigns/{campaignId}/publish", async (
            string campaignId,
            HttpContext context,
            ICampaignAdministration campaigns,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await CampaignWrite(
                campaignId,
                context,
                idempotency,
                $"campaigns:{campaignId}:publish",
                null,
                campaigns.PublishAsync,
                cancellationToken))
            .AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));

        api.MapPost("/campaigns/{campaignId}/close", async (
            string campaignId,
            HttpContext context,
            ICampaignAdministration campaigns,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await CampaignWrite(
                campaignId,
                context,
                idempotency,
                $"campaigns:{campaignId}:close",
                null,
                campaigns.CloseAsync,
                cancellationToken))
            .AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));

        api.MapPost("/campaigns/{campaignId}/cancel", async (
            string campaignId,
            CancelCampaignInput input,
            HttpContext context,
            ICampaignAdministration campaigns,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await CampaignWrite(
                campaignId,
                context,
                idempotency,
                $"campaigns:{campaignId}:cancel",
                input,
                (id, token) => campaigns.CancelAsync(id, input.Reason, token),
                cancellationToken))
            .AddEndpointFilter(new StaffRoleFilter(StaffRole.Owner));

        api.MapPost("/campaigns/{campaignId}/settle", async (
            string campaignId,
            HttpContext context,
            ICampaignAdministration campaigns,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
            await CampaignWrite(
                campaignId,
                context,
                idempotency,
                $"campaigns:{campaignId}:settle",
                null,
                campaigns.SettleAsync,
                cancellationToken))
            .AddEndpointFilter(new StaffRoleFilter(StaffRole.Accountant));

        api.MapGet("/campaigns/{campaignId}/offers", async (
            string campaignId,
            ICampaignAdministration campaigns,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(campaignId, out var parsed))
            {
                return BffHttp.Problem(new Error("campaign.not-found", "找不到指定的開團。"));
            }

            var result = await campaigns.GetOffersAsync(new CampaignId(parsed), cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : BffHttp.Problem(result.Error);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.ReadOnly));

        api.MapPost("/campaigns/{campaignId}/offers", async (
            string campaignId,
            CampaignOfferInput input,
            HttpContext context,
            ICampaignAdministration campaigns,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(campaignId, out var parsed))
            {
                return BffHttp.Problem(new Error("campaign.not-found", "找不到指定的開團。"));
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                Scope(context, $"campaigns:{campaignId}:offers:add"),
                input,
                token => campaigns.AddOfferAsync(new CampaignId(parsed), input, token),
                StatusCodes.Status201Created,
                cancellationToken);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));

        api.MapDelete("/campaigns/{campaignId}/offers/{offerId}", async (
            string campaignId,
            string offerId,
            HttpContext context,
            ICampaignAdministration campaigns,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(campaignId, out var campaign) || !TryId(offerId, out var offer))
            {
                return BffHttp.Problem(new Error("campaign.offer-not-found", "找不到指定的開團商品。"));
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                Scope(context, $"campaigns:{campaignId}:offers:{offerId}:delete"),
                request: null,
                token => campaigns.RemoveOfferAsync(
                    new CampaignId(campaign),
                    new CampaignOfferId(offer),
                    token),
                cancellationToken);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));
    }

    private static void MapLedger(RouteGroupBuilder api)
    {
        api.MapGet("/ledger/entries", async (
            string? sourceModule,
            string? sourceRef,
            DateOnly? from,
            DateOnly? to,
            string? cursor,
            int? limit,
            ILedgerQuery ledger,
            CancellationToken cancellationToken) =>
        {
            var result = await ledger.SearchAsync(
                new JournalSearch(sourceModule, sourceRef, from, to, cursor, limit ?? 20),
                cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new AdminJournalPage(
                    result.Value.Items.Select(entry => new AdminJournalEntryResponse(
                        entry.Id,
                        entry.OccurredAt,
                        entry.PostedAt,
                        entry.SourceModule,
                        entry.SourceRef,
                        entry.Memo,
                        entry.Lines.Select(line => new AdminJournalLineResponse(
                            line.AccountCode,
                            AccountName(line.AccountCode),
                            line.Direction,
                            line.Amount)).ToArray())).ToArray(),
                    result.Value.NextCursor))
                : BffHttp.Problem(result.Error);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.Accountant));

        api.MapGet("/ledger/campaign-margin/{campaignId}", async (
            string campaignId,
            ILedgerQuery ledger,
            CancellationToken cancellationToken) =>
        {
            if (!TryId(campaignId, out var parsed))
            {
                return BffHttp.Problem(new Error("campaign.not-found", "找不到指定的開團。"));
            }

            var result = await ledger.GetCampaignMarginAsync(new CampaignId(parsed), cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : BffHttp.Problem(result.Error);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.Accountant));

        api.MapGet("/ledger/liability-vs-cash", async (
            ILedgerQuery ledger,
            CancellationToken cancellationToken) =>
        {
            var result = await ledger.GetLiabilityVsCashAsync(cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : BffHttp.Problem(result.Error);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.Accountant));
    }

    private static async Task<IResult> CampaignWrite(
        string campaignId,
        HttpContext context,
        IIdempotencyStore idempotency,
        string operation,
        object? request,
        Func<CampaignId, CancellationToken, Task<Result<AdminCampaignView>>> action,
        CancellationToken cancellationToken)
    {
        if (!TryId(campaignId, out var parsed))
        {
            return BffHttp.Problem(new Error("campaign.not-found", "找不到指定的開團。"));
        }

        return await BffHttp.ExecuteIdempotentAsync(
            context,
            idempotency,
            Scope(context, operation),
            request,
            token => action(new CampaignId(parsed), token),
            StatusCodes.Status200OK,
            cancellationToken);
    }

    /// <summary>
    /// 端點層的 logger。<see cref="M1aEndpoints"/> 是 static class，不能當
    /// <c>ILogger&lt;T&gt;</c> 的型別引數，所以用固定的類別名稱字串建；
    /// 在 Map 階段建一次就好，不必每個請求重建。
    /// </summary>
    internal static ILogger LoggerFor(IEndpointRouteBuilder endpoints) =>
        endpoints.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GreyGray.Api.Admin.M1aEndpoints");

    private static async Task CreateSessionAsync(
        HttpContext context,
        ISessionStore sessions,
        StaffProfile staff,
        CancellationToken cancellationToken)
    {
        var token = await sessions.CreateAsync(
            staff.Id.ToString(),
            SessionSubjectKind.Staff,
            TenantId.Default,
            staff.Role.ToString(),
            cancellationToken);
        BffHttp.SetSessionCookie(context.Response, SessionCookie, token);
    }

    internal static string Scope(HttpContext context, string operation) =>
        $"admin:{GetStaffId(context)}:{operation}";

    private static StaffId GetStaffId(HttpContext context) =>
        context.Items[StaffItem] is StaffId id
            ? id
            : throw new InvalidOperationException("StaffRoleFilter 尚未建立員工身分。");

    internal static bool TryId(string raw, out Guid id) => Guid.TryParseExact(raw, "N", out id);

    private static string AccountName(string code) => code switch
    {
        AccountCodes.Cash => "現金／銀行存款",
        AccountCodes.InTransitECPay => "綠界在途",
        AccountCodes.InTransitNewebPay => "藍新在途",
        AccountCodes.InTransitLinePay => "LINE Pay 在途",
        AccountCodes.ChannelReceivable => "通路應收帳款",
        AccountCodes.Inventory => "存貨",
        AccountCodes.DeferredGoodsRevenue => "預收貨款",
        AccountCodes.DeferredShippingRevenue => "預收運費",
        AccountCodes.CustomerStoredValue => "客戶儲值金",
        AccountCodes.SalesRevenue => "銷貨收入",
        AccountCodes.ShippingRevenue => "運費收入",
        AccountCodes.CostOfGoodsSold => "銷貨成本",
        AccountCodes.ShippingCost => "運費成本",
        AccountCodes.TripCost => "旅程成本",
        AccountCodes.PaymentProcessingFee => "金流手續費",
        _ => code,
    };

    private static AdminSkuResponse ToAdminSku(AdminSkuView sku, int available = 0) => new(
        sku.Id,
        sku.Name,
        sku.VariantName,
        sku.WeightGram,
        sku.Size,
        sku.UnitOfMeasure,
        sku.UnitCount,
        sku.ListPrice,
        sku.IsActive,
        available);

    private static AdminProductResponse ToAdminProduct(
        AdminProductView product,
        IReadOnlyDictionary<SkuId, int>? availability = null) => new(
        product.Id,
        product.Name,
        product.Description,
        product.ShortDescription,
        product.CategoryId,
        product.Mode,
        product.Images,
        product.IsActive,
        product.Skus.Select(sku => ToAdminSku(
            sku,
            availability?.GetValueOrDefault(sku.Id) ?? 0)).ToArray());

    private static async Task<Result<IReadOnlyList<AdminProductResponse>>> ToAdminProductsAsync(
        IReadOnlyList<AdminProductView> products,
        IInventoryQuery inventory,
        CancellationToken cancellationToken)
    {
        var skuIds = products.SelectMany(product => product.Skus)
            .Select(sku => sku.Id)
            .Distinct()
            .ToArray();
        if (skuIds.Length == 0)
        {
            return products.Select(product => ToAdminProduct(product)).ToArray();
        }

        var result = await inventory.GetAvailabilityAsync(skuIds, cancellationToken);
        if (result.IsFailure)
        {
            return Result<IReadOnlyList<AdminProductResponse>>.Failure(result.Error);
        }

        var bySku = result.Value.ToDictionary(value => value.SkuId, value => value.Available);
        return products.Select(product => ToAdminProduct(product, bySku)).ToArray();
    }

    private static AdminCampaignDetailResponse ToAdminCampaignDetail(AdminCampaignDetail detail) => new(
        detail.Campaign.Id,
        detail.Campaign.Title,
        detail.Campaign.Destination,
        detail.Campaign.DepartAt,
        detail.Campaign.ReturnAt,
        detail.Campaign.ClosesAt,
        detail.Campaign.Status,
        detail.Campaign.OrderCount,
        detail.Campaign.TripCostTotal,
        detail.Campaign.Description,
        detail.Campaign.CoverImageUrl,
        detail.Offers);

    private static async Task<Result<IReadOnlyList<AdminOrderListItemResponse>>> ToAdminOrderListAsync(
        IReadOnlyList<OrderView> orders,
        ICustomerDirectory customers,
        CancellationToken cancellationToken)
    {
        var customerNames = new Dictionary<CustomerId, string>();
        foreach (var customerId in orders.Select(order => order.CustomerId).Distinct())
        {
            var customer = await customers.GetAsync(customerId, cancellationToken);
            if (customer.IsFailure)
            {
                return Result<IReadOnlyList<AdminOrderListItemResponse>>.Failure(customer.Error);
            }

            customerNames.Add(customerId, customer.Value.DisplayName);
        }

        return orders.Select(order => new AdminOrderListItemResponse(
                order.Id,
                order.OrderNumber,
                order.CustomerId,
                customerNames[order.CustomerId],
                order.Status,
                order.GrandTotal,
                order.PlacedAt,
                SingleCampaign(order)))
            .ToArray();
    }

    /// <summary>把 <see cref="OrderView"/> 組成後台 <c>AdminOrder</c> 回應。</summary>
    /// <remarks>
    /// <para>
    /// <c>internal</c>（原本 <c>private</c>）：ADR-026 的短缺退款端點另開在
    /// <see cref="M1bShortfallRefundEndpoints"/>，要共用同一份 AdminOrder 組裝邏輯。
    /// </para>
    /// <para>
    /// <b>BE-35：這個方法不會失敗。</b>取消／退款這幾條路呼叫它的時候，訂單變更與
    /// <c>RefundRequested</c> 都已經 commit 了，讓組回應把它們報成失敗正是
    /// 「現在卡在哪 #22」那一整個家族（A3／A4／A20）。
    /// </para>
    /// <para>
    /// 讀不到 customer／SKU／payment 時填退化值，兩條硬性要求：<b>① 不改契約</b>——
    /// 退化回應仍符合 <c>docs/api/openapi.admin.yaml</c> 的 <c>AdminOrder</c> schema，
    /// 必填欄位一律有值（<c>customerDisplayName</c> 用 <c>customerId</c> 的字串形式、
    /// 品項 <c>name</c> 用 <c>skuId</c> 的字串形式，<c>payments</c> 退成空陣列）；
    /// <b>② 不准靜默</b>——每一次退化都 <c>LogError</c> 留痕。
    /// </para>
    /// <para>
    /// <c>skuById</c> 改用 <c>TryGetValue</c>：原本的 <c>skuById[line.SkuId]</c> 在
    /// <c>GetSkusAsync</c> 成功卻少回某一筆時會丟 <c>KeyNotFoundException</c>，
    /// 而那正是「render 丟例外」那條路，不該留著。
    /// </para>
    /// </remarks>
    internal static async Task<AdminOrderResponse> ToAdminOrderAsync(
        OrderView order,
        ICustomerDirectory customers,
        IPaymentQuery payments,
        ICatalogQuery catalog,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(order.CustomerId, cancellationToken);
        if (customer.IsFailure)
        {
            logger.LogError(
                "訂單 {OrderId} 讀不到客戶 {CustomerId}（{ErrorCode}），改以 CustomerId 當顯示名；" +
                "訂單本身的變更已經成立。",
                order.Id,
                order.CustomerId,
                customer.Error.Code);
        }

        var skus = await catalog.GetSkusAsync(
            order.Lines.Select(line => line.SkuId).Distinct().ToArray(),
            cancellationToken);
        if (skus.IsFailure)
        {
            logger.LogError(
                "訂單 {OrderId} 讀不到商品資料（{ErrorCode}），品項改以 SkuId 當顯示名。",
                order.Id,
                skus.Error.Code);
        }

        var paymentResult = await payments.GetByOrderAsync(order.Id, cancellationToken);
        if (paymentResult.IsFailure)
        {
            logger.LogError(
                "訂單 {OrderId} 讀不到付款紀錄（{ErrorCode}），回應的 payments 退成空陣列。",
                order.Id,
                paymentResult.Error.Code);
        }

        var skuById = skus.IsSuccess
            ? skus.Value.ToDictionary(sku => sku.Id)
            : [];
        return new AdminOrderResponse(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            customer.IsSuccess ? customer.Value.DisplayName : order.CustomerId.ToString(),
            order.Status,
            order.GrandTotal,
            order.PlacedAt,
            SingleCampaign(order),
            order.GoodsTotal,
            order.ShippingFee,
            order.DeliveryMethod,
            order.ShippingPolicy,
            order.ConvenienceStoreCode,
            order.ConvenienceStoreName,
            order.ConvenienceStoreAddress,
            order.Lines.Select(line =>
            {
                string name;
                string? variantName;
                if (skuById.TryGetValue(line.SkuId, out var sku))
                {
                    name = sku.Name;
                    variantName = sku.VariantName;
                }
                else
                {
                    if (skus.IsSuccess)
                    {
                        logger.LogError(
                            "訂單 {OrderId} 的品項 {OrderLineId} 在商品查詢結果裡找不到 SKU {SkuId}，" +
                            "改以 SkuId 當顯示名。",
                            order.Id,
                            line.Id,
                            line.SkuId);
                    }

                    name = line.SkuId.ToString();
                    variantName = null;
                }

                return new AdminOrderLineResponse(
                    line.Id,
                    line.SkuId,
                    name,
                    variantName,
                    line.Mode,
                    line.Status,
                    line.Quantity,
                    line.QuantityShortfall,
                    line.UnitPrice,
                    line.LineTotal,
                    line.RefundedAmount,
                    line.CampaignId,
                    line.ConsumedLot);
            }).ToArray(),
            paymentResult.IsSuccess
                ? paymentResult.Value.Select(payment => new AdminPaymentSummaryResponse(
                    payment.Id,
                    payment.Provider,
                    payment.Status,
                    payment.Amount,
                    payment.Fee,
                    payment.ProviderTransactionId,
                    payment.CapturedAt,
                    payment.SettledAt,
                    payment.Method,
                    payment.Instructions is null
                        ? null
                        : new AdminPaymentInstructionsResponse(
                            payment.Instructions.Method,
                            payment.Instructions.BankCode,
                            payment.Instructions.VirtualAccount,
                            payment.Instructions.PaymentNo,
                            payment.Instructions.Barcodes ?? [],
                            payment.Instructions.ExpiresAt,
                            payment.Instructions.IssuedAt))).ToArray()
                : [],
            order.QuoteExplain,
            // ADR-039：後台全員直接看得到明文，不遮罩、不加解鎖閘門。出貨要用這兩個值——
            // 姓名與證件不符時超商會拒絕交貨。
            order.RecipientName,
            order.RecipientPhone,
            // 宅配的收件地址：沒有這個欄位以前，後台看得到姓名電話與超商門市，
            // 唯獨宅配地址看不到——出貨的人還是寄不了宅配。
            order.RecipientAddress,
            // 契約保留這個欄位只為相容既有用戶端，恆為 null（舊描述承諾的「填存取理由才看得到
            // 明文」那個端點從來沒有實作過，而且與 ADR-039 的決定矛盾）。
            CustomerContactMasked: null,
            PaymentDueAt: order.PaymentDueAt,
            CancellationSource: order.CancellationSource);
    }

    private static CampaignId? SingleCampaign(OrderView order)
    {
        var campaignIds = order.Lines
            .Where(line => line.CampaignId is not null)
            .Select(line => line.CampaignId!.Value)
            .Distinct()
            .Take(2)
            .ToArray();
        return campaignIds.Length == 1 ? campaignIds[0] : null;
    }

    private sealed record CancelCampaignInput(string Reason);

    internal sealed record CancelOrderInput(string Reason, RefundDestination RefundTo);

    private sealed record AdminProductRequest(
        string Name,
        string? Description,
        string? ShortDescription,
        CategoryId? CategoryId,
        FulfillmentMode Mode,
        IReadOnlyList<string>? Images,
        bool? IsActive)
    {
        public AdminProductInput ToContract() => new(
            Name,
            Description,
            ShortDescription,
            CategoryId,
            Mode,
            Images,
            IsActive ?? true);
    }

    internal sealed record AdminSkuRequest(
        string Name,
        string? VariantName,
        int WeightGram,
        Dimensions Size,
        string? UnitOfMeasure,
        int? UnitCount,
        Money? ListPrice,
        bool? IsActive)
    {
        public AdminSkuInput ToContract() => new(
            Name,
            VariantName,
            WeightGram,
            Size,
            UnitOfMeasure,
            UnitCount,
            ListPrice,
            IsActive ?? true);
    }

    private sealed record AdminProductPage(
        IReadOnlyList<AdminProductResponse> Items,
        string? NextCursor);

    private sealed record AdminJournalPage(
        IReadOnlyList<AdminJournalEntryResponse> Items,
        string? NextCursor);

    private sealed record AdminJournalEntryResponse(
        EntryId Id,
        DateTimeOffset OccurredAt,
        DateTimeOffset PostedAt,
        string SourceModule,
        string SourceRef,
        string Memo,
        IReadOnlyList<AdminJournalLineResponse> Lines);

    private sealed record AdminJournalLineResponse(
        string AccountCode,
        string AccountName,
        Direction Direction,
        Money Amount);

    private sealed record AdminSkuResponse(
        SkuId Id,
        string Name,
        string? VariantName,
        int WeightGram,
        Dimensions Size,
        string? UnitOfMeasure,
        int? UnitCount,
        Money? ListPrice,
        bool IsActive,
        int Available);

    private sealed record AdminProductResponse(
        ProductId Id,
        string Name,
        string? Description,
        string? ShortDescription,
        CategoryId? CategoryId,
        FulfillmentMode Mode,
        IReadOnlyList<string> Images,
        bool IsActive,
        IReadOnlyList<AdminSkuResponse> Skus);

    private sealed record AdminCampaignDetailResponse(
        CampaignId Id,
        string Title,
        string Destination,
        DateOnly DepartAt,
        DateOnly ReturnAt,
        DateTimeOffset ClosesAt,
        CampaignStatus Status,
        int OrderCount,
        Money? TripCostTotal,
        string? Description,
        string? CoverImageUrl,
        IReadOnlyList<AdminCampaignOfferView> Offers);

    private sealed record AdminOrderPage(
        IReadOnlyList<AdminOrderListItemResponse> Items,
        string? NextCursor);

    private sealed record AdminOrderListItemResponse(
        OrderId Id,
        string OrderNumber,
        CustomerId CustomerId,
        string CustomerDisplayName,
        OrderStatus Status,
        Money GrandTotal,
        DateTimeOffset PlacedAt,
        CampaignId? CampaignId);

    internal sealed record AdminOrderLineResponse(
        OrderLineId Id,
        SkuId SkuId,
        string Name,
        string? VariantName,
        FulfillmentMode Mode,
        OrderLineStatus Status,
        int Quantity,
        int QuantityShortfall,
        Money UnitPrice,
        Money LineTotal,
        Money? RefundedAmount,
        CampaignId? CampaignId,
        LotId? ConsumedLotId);

    internal sealed record AdminPaymentSummaryResponse(
        PaymentId Id,
        PaymentProvider Provider,
        PaymentStatus Status,
        Money Amount,
        Money? Fee,
        string? ProviderTransactionId,
        DateTimeOffset? CapturedAt,
        DateTimeOffset? SettledAt,
        PaymentMethod? Method = null,
        AdminPaymentInstructionsResponse? Instructions = null);

    internal sealed record AdminPaymentInstructionsResponse(
        PaymentMethod Method,
        string? BankCode,
        string? VirtualAccount,
        string? PaymentNo,
        IReadOnlyList<string> Barcodes,
        DateTimeOffset ExpiresAt,
        DateTimeOffset IssuedAt);

    internal sealed record AdminOrderResponse(
        OrderId Id,
        string OrderNumber,
        CustomerId CustomerId,
        string CustomerDisplayName,
        OrderStatus Status,
        Money GrandTotal,
        DateTimeOffset PlacedAt,
        CampaignId? CampaignId,
        Money GoodsTotal,
        Money ShippingFee,
        DeliveryMethod DeliveryMethod,
        ShippingPolicy ShippingPolicy,
        string? ConvenienceStoreCode,
        string? ConvenienceStoreName,
        string? ConvenienceStoreAddress,
        IReadOnlyList<AdminOrderLineResponse> Lines,
        IReadOnlyList<AdminPaymentSummaryResponse> Payments,
        IReadOnlyList<string> QuoteExplain,
        string? RecipientName,
        string? RecipientPhone,
        string? RecipientAddress,
        string? CustomerContactMasked,
        DateTimeOffset? PaymentDueAt = null,
        OrderCancellationSource? CancellationSource = null);

    internal sealed class StaffRoleFilter(StaffRole requiredRole) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(
            EndpointFilterInvocationContext invocationContext,
            EndpointFilterDelegate next)
        {
            var context = invocationContext.HttpContext;
            var sessions = context.RequestServices.GetRequiredService<ISessionStore>();
            var session = await BffHttp.GetSessionAsync(
                context,
                sessions,
                SessionCookie,
                SessionSubjectKind.Staff,
                context.RequestAborted);
            if (session is null || !Guid.TryParseExact(session.SubjectId, "N", out var staffGuid))
            {
                return BffHttp.Unauthorized();
            }

            var staffId = new StaffId(staffGuid);
            var directory = context.RequestServices.GetRequiredService<IStaffDirectory>();
            var role = await directory.GetRoleAsync(staffId, context.RequestAborted);
            if (role.IsFailure)
            {
                return BffHttp.Unauthorized();
            }

            var policy = context.RequestServices.GetRequiredService<IStaffRolePolicy>();
            if (!policy.Allows(role.Value, requiredRole))
            {
                return BffHttp.Forbidden();
            }

            context.Items[StaffItem] = staffId;
            return await next(invocationContext);
        }
    }
}
