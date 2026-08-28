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

            var projected = await ToAdminOrderAsync(
                result.Value,
                customers,
                payments,
                catalog,
                cancellationToken);
            return projected.IsSuccess
                ? Results.Ok(projected.Value)
                : BffHttp.Problem(projected.Error);
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
                        return Result<AdminOrderResponse>.Failure(existing.Error);
                    }

                    if (input.RefundTo == RefundDestination.OriginalPaymentMethod &&
                        existing.Value.PaidAmount is { IsZero: false })
                    {
                        return Result<AdminOrderResponse>.Failure(
                            "payment.original-refund-not-configured",
                            "綠界原路退款尚未完成 provider API 設定，訂單未取消；可改選退款至儲值金。");
                    }

                    var cancelled = await ordering.CancelAdminAsync(
                        new OrderId(parsed),
                        input.Reason,
                        input.RefundTo,
                        token);
                    return cancelled.IsSuccess
                        ? await ToAdminOrderAsync(
                            cancelled.Value,
                            customers,
                            payments,
                            catalog,
                            token)
                        : Result<AdminOrderResponse>.Failure(cancelled.Error);
                },
                StatusCodes.Status200OK,
                cancellationToken);
        }).AddEndpointFilter(new StaffRoleFilter(StaffRole.Operator));
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

    private static async Task<Result<AdminOrderResponse>> ToAdminOrderAsync(
        OrderView order,
        ICustomerDirectory customers,
        IPaymentQuery payments,
        ICatalogQuery catalog,
        CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(order.CustomerId, cancellationToken);
        if (customer.IsFailure)
        {
            return Result<AdminOrderResponse>.Failure(customer.Error);
        }

        var skus = await catalog.GetSkusAsync(
            order.Lines.Select(line => line.SkuId).Distinct().ToArray(),
            cancellationToken);
        if (skus.IsFailure)
        {
            return Result<AdminOrderResponse>.Failure(skus.Error);
        }

        var paymentResult = await payments.GetByOrderAsync(order.Id, cancellationToken);
        if (paymentResult.IsFailure)
        {
            return Result<AdminOrderResponse>.Failure(paymentResult.Error);
        }

        var skuById = skus.Value.ToDictionary(sku => sku.Id);
        return new AdminOrderResponse(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            customer.Value.DisplayName,
            order.Status,
            order.GrandTotal,
            order.PlacedAt,
            SingleCampaign(order),
            order.GoodsTotal,
            order.ShippingFee,
            order.DeliveryMethod,
            order.ShippingPolicy,
            order.Lines.Select(line =>
            {
                var sku = skuById[line.SkuId];
                return new AdminOrderLineResponse(
                    line.Id,
                    line.SkuId,
                    sku.Name,
                    sku.VariantName,
                    line.Mode,
                    line.Status,
                    line.Quantity,
                    line.UnitPrice,
                    line.LineTotal,
                    line.CampaignId,
                    line.ConsumedLot);
            }).ToArray(),
            paymentResult.Value.Select(payment => new AdminPaymentSummaryResponse(
                payment.Id,
                payment.Provider,
                payment.Status,
                payment.Amount,
                payment.Fee,
                payment.ProviderTransactionId,
                payment.CapturedAt,
                payment.SettledAt)).ToArray(),
            order.QuoteExplain,
            CustomerContactMasked: null);
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

    private sealed record CancelOrderInput(string Reason, RefundDestination RefundTo);

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

    private sealed record AdminSkuRequest(
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

    private sealed record AdminOrderLineResponse(
        OrderLineId Id,
        SkuId SkuId,
        string Name,
        string? VariantName,
        FulfillmentMode Mode,
        OrderLineStatus Status,
        int Quantity,
        Money UnitPrice,
        Money LineTotal,
        CampaignId? CampaignId,
        LotId? ConsumedLotId);

    private sealed record AdminPaymentSummaryResponse(
        PaymentId Id,
        PaymentProvider Provider,
        PaymentStatus Status,
        Money Amount,
        Money? Fee,
        string? ProviderTransactionId,
        DateTimeOffset? CapturedAt,
        DateTimeOffset? SettledAt);

    private sealed record AdminOrderResponse(
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
        IReadOnlyList<AdminOrderLineResponse> Lines,
        IReadOnlyList<AdminPaymentSummaryResponse> Payments,
        IReadOnlyList<string> QuoteExplain,
        string? CustomerContactMasked);

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
