using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;

namespace GreyGray.Api.Admin;

internal static class M1bProcurementEndpoints
{
    public static IEndpointRouteBuilder MapM1bProcurementEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/v1");

        api.MapGet("/campaigns/{campaignId}/purchase-items", async (
            string campaignId,
            PurchaseItemStatus? status,
            IProcurementQuery procurement,
            IOrderQuery orders,
            ICatalogQuery catalog,
            CancellationToken cancellationToken) =>
        {
            if (!M1aEndpoints.TryId(campaignId, out var parsedCampaignId))
            {
                return BffHttp.Problem(new Error(
                    "procurement.campaign-not-found",
                    "找不到指定的開團。"));
            }

            var id = new CampaignId(parsedCampaignId);
            var items = await procurement.GetCampaignListAsync(id, cancellationToken);
            if (items.IsFailure)
            {
                return BffHttp.Problem(items.Error);
            }

            var selected = status is null
                ? items.Value
                : items.Value.Where(item => item.Status == status.Value).ToArray();
            if (selected.Count == 0)
            {
                return Results.Ok(Array.Empty<PurchaseItemResponse>());
            }

            var skus = await catalog.GetSkusAsync(
                selected.Select(item => item.SkuId).Distinct().ToArray(),
                cancellationToken);
            if (skus.IsFailure)
            {
                return BffHttp.Problem(skus.Error);
            }

            var campaignOrders = await orders.GetByCampaignAsync(id, cancellationToken);
            if (campaignOrders.IsFailure)
            {
                return BffHttp.Problem(campaignOrders.Error);
            }

            var skuById = skus.Value.ToDictionary(sku => sku.Id);
            var orderNumberByLine = campaignOrders.Value
                .SelectMany(order => order.Lines.Select(line => new { line.Id, order.OrderNumber }))
                .ToDictionary(row => row.Id, row => row.OrderNumber);
            var response = selected.Select(item =>
            {
                var sku = skuById[item.SkuId];
                return new PurchaseItemResponse(
                    item.Id,
                    item.SkuId,
                    sku.Name,
                    sku.VariantName,
                    null,
                    item.OrderLineId,
                    orderNumberByLine.GetValueOrDefault(item.OrderLineId) ?? string.Empty,
                    item.QuantityRequested,
                    item.QuantityPurchased,
                    item.TargetPrice,
                    item.Status,
                    item.DecidedAt,
                    null);
            }).ToArray();

            return Results.Ok(response);
        }).AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.Operator));

        api.MapPost("/purchase-items/{purchaseItemId}/purchased", async (
            string purchaseItemId,
            MarkPurchasedInput input,
            HttpContext context,
            IProcurementApplication procurement,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            if (!M1aEndpoints.TryId(purchaseItemId, out var parsedPurchaseItemId))
            {
                return BffHttp.Problem(new Error(
                    "procurement.purchase-item-not-found",
                    "找不到指定的採購品項。"));
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                M1aEndpoints.Scope(context, $"purchase-items:{purchaseItemId}:purchased"),
                input,
                async token =>
                {
                    var result = await procurement.MarkPurchasedAsync(
                        new PurchaseItemId(parsedPurchaseItemId),
                        input.QuantityPurchased,
                        new MoneyPair(input.ActualPaidOriginal, input.ActualPaidBooking, null),
                        token);
                    return result.IsSuccess
                        ? new PurchaseRecordedResponse()
                        : Result<PurchaseRecordedResponse>.Failure(result.Error);
                },
                StatusCodes.Status200OK,
                cancellationToken);
        }).AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.Operator));

        return endpoints;
    }

    private sealed record MarkPurchasedInput(
        int QuantityPurchased,
        Money ActualPaidOriginal,
        Money ActualPaidBooking);

    private sealed record PurchaseRecordedResponse;

    private sealed record PurchaseItemResponse(
        PurchaseItemId Id,
        SkuId SkuId,
        string Name,
        string? VariantName,
        string? ImageUrl,
        OrderLineId OrderLineId,
        string OrderNumber,
        int QuantityRequested,
        int QuantityPurchased,
        Money? TargetPrice,
        PurchaseItemStatus Status,
        DateTimeOffset? DecidedAt,
        InquiryResponse? Inquiry);

    private sealed record InquiryResponse(
        InquiryId Id,
        Money OriginalPrice,
        Money NewPrice,
        DateTimeOffset AskedAt,
        DateTimeOffset TimeoutAt,
        DateTimeOffset? RepliedAt,
        InquiryOutcome? Outcome,
        string? ReplyText);
}
