using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;

namespace GreyGray.Api.Admin;

internal static class M1bFulfillmentEndpoints
{
    public static IEndpointRouteBuilder MapM1bFulfillmentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/v1");

        api.MapGet("/shipments", async (
            ShipmentStatus? status,
            string? cursor,
            int? limit,
            IFulfillmentQuery fulfillment,
            CancellationToken cancellationToken) =>
        {
            ShipmentId? pageCursor = null;
            if (cursor is not null)
            {
                if (!M1aEndpoints.TryId(cursor, out var parsedCursor))
                {
                    return BffHttp.Problem(new Error(
                        "fulfillment.invalid-cursor",
                        "分頁游標格式錯誤。"));
                }

                pageCursor = new ShipmentId(parsedCursor);
            }

            var result = await fulfillment.ListAsync(
                new AdminShipmentListRequest(status, pageCursor, limit ?? 20),
                cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ShipmentPageResponse(result.Value.Items, result.Value.NextCursor))
                : BffHttp.Problem(result.Error);
        }).AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.Operator));

        // 單筆查詢（第七波，契約由 BE-19 加、接線由整合者代為套用：INT-5）。
        // 沒有這一條時前端只能撈 limit:100 的列表再自己過濾，出貨單超過 100 張就會白頁。
        api.MapGet("/shipments/{shipmentId}", async (
            string shipmentId,
            IFulfillmentQuery fulfillment,
            CancellationToken cancellationToken) =>
        {
            if (!M1aEndpoints.TryId(shipmentId, out var parsed))
            {
                return BffHttp.Problem(new Error(
                    "fulfillment.shipment-not-found",
                    "找不到指定的出貨單。"));
            }

            var result = await fulfillment.GetAsync(new ShipmentId(parsed), cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : BffHttp.Problem(result.Error);
        }).AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.Operator));

        // 冪等鍵自己讀一次再傳進模組（比照 Storefront 的 CompleteCheckoutAsync）：
        // BffHttp 的冪等在收尾階段出錯時會 abandon 這把鍵，同一把鍵重送就會
        // 再建一張出貨單，所以 Fulfillment 模組自己也要拿得到它（見 docs/32 §1）。
        api.MapPost("/shipments", async (
            CreateShipmentInput input,
            HttpContext context,
            IFulfillmentApplication fulfillment,
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
                M1aEndpoints.Scope(context, "shipments:create"),
                input,
                async token =>
                {
                    var created = await fulfillment.CreateAsync(
                        input.OrderIds,
                        input.Method,
                        key.ToString().Trim(),
                        token);
                    return created.IsSuccess
                        ? created.Value
                        : Result<ShipmentSummary>.Failure(created.Error);
                },
                StatusCodes.Status201Created,
                cancellationToken);
        }).AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.Operator));

        api.MapPost("/shipments/{shipmentId}/dispatch", async (
            string shipmentId,
            DispatchShipmentInput input,
            HttpContext context,
            IFulfillmentApplication fulfillment,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            if (!M1aEndpoints.TryId(shipmentId, out var parsed))
            {
                return BffHttp.Problem(new Error(
                    "fulfillment.shipment-not-found",
                    "找不到指定的出貨單。"));
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                M1aEndpoints.Scope(context, $"shipments:{shipmentId}:dispatch"),
                input,
                async token =>
                {
                    var dispatched = await fulfillment.DispatchAsync(
                        new ShipmentId(parsed),
                        input.TrackingNumber,
                        input.CarrierCost,
                        token);
                    return dispatched.IsSuccess
                        ? dispatched.Value
                        : Result<ShipmentSummary>.Failure(dispatched.Error);
                },
                StatusCodes.Status200OK,
                cancellationToken);
        }).AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.Operator));

        api.MapPost("/shipments/{shipmentId}/deliver", async (
            string shipmentId,
            HttpContext context,
            IFulfillmentApplication fulfillment,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            if (!M1aEndpoints.TryId(shipmentId, out var parsed))
            {
                return BffHttp.Problem(new Error(
                    "fulfillment.shipment-not-found",
                    "找不到指定的出貨單。"));
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                M1aEndpoints.Scope(context, $"shipments:{shipmentId}:deliver"),
                request: null,
                async token =>
                {
                    var delivered = await fulfillment.DeliverAsync(new ShipmentId(parsed), token);
                    return delivered.IsSuccess
                        ? delivered.Value
                        : Result<ShipmentSummary>.Failure(delivered.Error);
                },
                StatusCodes.Status200OK,
                cancellationToken);
        }).AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.Operator));

        return endpoints;
    }

    private sealed record ShipmentPageResponse(
        IReadOnlyList<ShipmentSummary> Items,
        string? NextCursor);

    private sealed record CreateShipmentInput(
        IReadOnlyList<OrderId> OrderIds,
        DeliveryMethod Method);

    private sealed record DispatchShipmentInput(
        string TrackingNumber,
        Money CarrierCost);
}
