using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;

namespace GreyGray.Api.Admin;

/// <summary>
/// M1b-2：缺貨補償與現場漲價詢問（ADR-023）。
///
/// <b>「標記缺貨」不承載、不預設退款去向。</b>
/// 客人選出來之前，退款流程一律不觸發——實際退款與 OrderLine 取消
/// 走既有的 <c>POST /v1/orders/{orderId}/lines/{lineId}/cancel</c>（M1a-6，已支援 refundTo），
/// 這裡只記「這個採購品項買不到」的事實，供現場後續操作。
/// </summary>
internal static class M1bCompensationEndpoints
{
    public static IEndpointRouteBuilder MapM1bCompensationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/v1");

        api.MapPost("/purchase-items/{purchaseItemId}/unavailable", async (
            string purchaseItemId,
            MarkUnavailableInput input,
            HttpContext context,
            IProcurementCompensation procurement,
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
                M1aEndpoints.Scope(context, $"purchase-items:{purchaseItemId}:unavailable"),
                input,
                async token =>
                {
                    var result = await procurement.MarkUnavailableAsync(
                        new PurchaseItemId(parsedPurchaseItemId),
                        input.Reason,
                        token);
                    return result.IsSuccess
                        ? new UnavailableRecordedResponse()
                        : Result<UnavailableRecordedResponse>.Failure(result.Error);
                },
                StatusCodes.Status200OK,
                cancellationToken);
        }).AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.Operator));

        api.MapPost("/purchase-items/{purchaseItemId}/price-changed", async (
            string purchaseItemId,
            ReportPriceChangedInput input,
            HttpContext context,
            IProcurementCompensation procurement,
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
                M1aEndpoints.Scope(context, $"purchase-items:{purchaseItemId}:price-changed"),
                input,
                async token =>
                {
                    var result = await procurement.ReportPriceChangedAsync(
                        new PurchaseItemId(parsedPurchaseItemId),
                        input.NewPrice,
                        token);
                    return result.IsSuccess
                        ? new PriceChangedResponse(result.Value.Id, result.Value.TimeoutAt)
                        : Result<PriceChangedResponse>.Failure(result.Error);
                },
                StatusCodes.Status200OK,
                cancellationToken);
        }).AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.Operator));

        return endpoints;
    }

    private sealed record MarkUnavailableInput(string Reason);

    private sealed record ReportPriceChangedInput(Money NewPrice);

    private sealed record UnavailableRecordedResponse;

    private sealed record PriceChangedResponse(InquiryId InquiryId, DateTimeOffset TimeoutAt);
}
