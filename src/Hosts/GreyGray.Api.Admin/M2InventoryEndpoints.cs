using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;

namespace GreyGray.Api.Admin;

/// <summary>
/// M2 批發進貨與批號列表（<c>docs/api/openapi.admin.yaml</c> 第 863 行起）。
/// <para>
/// <b>這是目前唯一能讓本地現貨進到庫存裡的路徑。</b>在這之前程式裡只有代購流程的
/// <c>GoodsReceived</c> 會建立批號，所以前台每個 SKU 的 <c>available</c> 都是 0，
/// 單品頁一律顯示「已售完」。
/// </para>
/// </summary>
internal static class M2InventoryEndpoints
{
    public static IEndpointRouteBuilder MapM2InventoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/v1");
        var logger = endpoints.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GreyGray.Api.Admin.M2InventoryEndpoints");

        api.MapGet("/lots", async (
            string? skuId,
            string? cursor,
            int? limit,
            IInventoryLotQuery inventory,
            ICatalogQuery catalog,
            CancellationToken cancellationToken) =>
        {
            SkuId? sku = null;
            if (skuId is not null)
            {
                if (!M1aEndpoints.TryId(skuId, out var parsedSku))
                {
                    return BffHttp.Problem(new Error(
                        "catalog.sku-not-found",
                        "SKU 識別格式錯誤。"));
                }

                sku = new SkuId(parsedSku);
            }

            LotId? pageCursor = null;
            if (cursor is not null)
            {
                if (!M1aEndpoints.TryId(cursor, out var parsedCursor))
                {
                    return BffHttp.Problem(new Error(
                        "inventory.invalid-cursor",
                        "分頁游標格式錯誤。"));
                }

                pageCursor = new LotId(parsedCursor);
            }

            var result = await inventory.ListLotsAsync(
                new AdminLotListRequest(sku, pageCursor, Math.Clamp(limit ?? 20, 1, 100)),
                cancellationToken);
            if (result.IsFailure)
            {
                return BffHttp.Problem(result.Error);
            }

            var items = await ToLotResponsesAsync(
                result.Value.Items,
                catalog,
                logger,
                cancellationToken);
            return Results.Ok(new LotPageResponse(items, result.Value.NextCursor));
        }).AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.ReadOnly));

        // 冪等鍵自己讀一次再傳進模組（比照 POST /v1/shipments，見 docs/34 §2）：
        // BffHttp 的冪等在收尾階段出錯時會 abandon 這把鍵，同一把鍵重送就會再建一個
        // 批號——幽靈庫存加一筆多出來的存貨分錄，所以 Inventory 模組自己也要拿得到它。
        api.MapPost("/lots", async (
            CreateLotInput input,
            HttpContext context,
            IInventoryReceiving inventory,
            ICatalogQuery catalog,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var key))
            {
                return BffHttp.Problem(
                    new Error("platform.idempotency-key-required", "缺少 Idempotency-Key header。"),
                    StatusCodes.Status400BadRequest);
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                M1aEndpoints.Scope(context, "lots:create"),
                input,
                async token =>
                {
                    var created = await inventory.ReceiveWholesaleAsync(
                        new WholesaleReceipt(
                            input.SkuId,
                            input.Quantity,
                            input.UnitCost,
                            input.BatchCode),
                        key.ToString().Trim(),
                        token);
                    if (created.IsFailure)
                    {
                        return Result<LotResponse>.Failure(created.Error);
                    }

                    var items = await ToLotResponsesAsync([created.Value], catalog, logger, token);
                    return items[0];
                },
                StatusCodes.Status201Created,
                cancellationToken);
        }).AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.Operator));

        return endpoints;
    }

    /// <summary>
    /// 補上 <c>Lot</c> schema 的選填欄位 <c>skuName</c>。<b>不做跨 schema JOIN</b>——
    /// 走 <see cref="ICatalogQuery.GetSkusAsync"/>，跟 <c>M1bProcurementEndpoints</c> 一樣。
    /// <para>
    /// 讀不到商品資料時<b>退化而不失敗</b>（BE-35 的判準）：批號本身已經建好或已經查到了，
    /// 讓組回應把它報成失敗就是 #22 那一整個家族。退化值用 <c>skuId</c> 的字串形式，
    /// 回應仍符合 schema；每一次退化都 <c>LogError</c> 留痕，不准靜默。
    /// </para>
    /// </summary>
    private static async Task<IReadOnlyList<LotResponse>> ToLotResponsesAsync(
        IReadOnlyList<Lot> lots,
        ICatalogQuery catalog,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (lots.Count == 0)
        {
            return [];
        }

        var skus = await catalog.GetSkusAsync(
            lots.Select(lot => lot.SkuId).Distinct().ToArray(),
            cancellationToken);
        if (skus.IsFailure)
        {
            logger.LogError(
                "批號列表讀不到商品資料（{ErrorCode}），skuName 改以 SkuId 當顯示名。",
                skus.Error.Code);
        }

        var skuById = skus.IsSuccess
            ? skus.Value.ToDictionary(sku => sku.Id)
            : [];
        return lots.Select(lot => new LotResponse(
                lot.Id,
                lot.SkuId,
                skuById.TryGetValue(lot.SkuId, out var sku) ? sku.Name : lot.SkuId.ToString(),
                lot.Source,
                lot.UnitCost,
                lot.QuantityOnHand,
                lot.QuantityReserved,
                lot.QuantityAvailable,
                lot.BatchCode,
                lot.FromCampaign,
                lot.ReceivedAt))
            .ToArray();
    }

    /// <summary>對齊 <c>openapi.admin.yaml</c> 的 <c>Lot</c> schema。</summary>
    private sealed record LotResponse(
        LotId Id,
        SkuId SkuId,
        string SkuName,
        LotSource Source,
        Money UnitCost,
        int QuantityOnHand,
        int QuantityReserved,
        int QuantityAvailable,
        string? BatchCode,
        CampaignId? FromCampaignId,
        DateTimeOffset ReceivedAt);

    private sealed record LotPageResponse(
        IReadOnlyList<LotResponse> Items,
        string? NextCursor);

    private sealed record CreateLotInput(
        SkuId SkuId,
        int Quantity,
        Money UnitCost,
        string? BatchCode);
}
