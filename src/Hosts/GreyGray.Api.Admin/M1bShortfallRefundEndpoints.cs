using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;

namespace GreyGray.Api.Admin;

/// <summary>
/// ADR-026：部分買到的短缺數量退款。
///
/// <b>「標記買到 3／5」不承載、不預設退款去向</b>——比照 M1b-2 缺貨補償的決策延後模式
/// （見 <see cref="M1bCompensationEndpoints"/>）：記錄買到結果時短缺的 2 件先掛著，
/// 客人選好退款去向之後才呼叫這裡，這一刻才真的退款並把訂單金額減下來。
///
/// 退款本身重用既有的 <c>RefundRequested</c> 事件與下游 Payment／Ledger 消費者，
/// 不是另一套退款機制。
/// </summary>
internal static class M1bShortfallRefundEndpoints
{
    public static IEndpointRouteBuilder MapM1bShortfallRefundEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/v1");

        api.MapPost("/orders/{orderId}/lines/{lineId}/refund-shortfall", async (
            string orderId,
            string lineId,
            M1aEndpoints.CancelOrderInput input,
            HttpContext context,
            IOrderingApplication ordering,
            ICustomerDirectory customers,
            IPaymentQuery payments,
            ICatalogQuery catalog,
            IIdempotencyStore idempotency,
            CancellationToken cancellationToken) =>
        {
            if (!M1aEndpoints.TryId(orderId, out var parsedOrder))
            {
                return BffHttp.Problem(new Error("ordering.order-not-found", "找不到指定的訂單。"));
            }

            if (!M1aEndpoints.TryId(lineId, out var parsedLine))
            {
                return BffHttp.Problem(new Error(
                    "ordering.order-line-not-found",
                    "找不到指定的訂單品項。"));
            }

            return await BffHttp.ExecuteIdempotentAsync(
                context,
                idempotency,
                M1aEndpoints.Scope(context, $"orders:{orderId}:lines:{lineId}:refund-shortfall"),
                input,
                async token =>
                {
                    var refunded = await ordering.RefundLineShortfallAsync(
                        new OrderId(parsedOrder),
                        new OrderLineId(parsedLine),
                        input.Reason,
                        input.RefundTo,
                        token);
                    return refunded.IsSuccess
                        ? await M1aEndpoints.ToAdminOrderAsync(
                            refunded.Value,
                            customers,
                            payments,
                            catalog,
                            token)
                        : Result<M1aEndpoints.AdminOrderResponse>.Failure(refunded.Error);
                },
                StatusCodes.Status200OK,
                cancellationToken);
        }).AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.Operator));

        return endpoints;
    }
}
