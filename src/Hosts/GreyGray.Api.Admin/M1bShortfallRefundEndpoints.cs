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
        var logger = M1aEndpoints.LoggerFor(api);

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
            await RefundLineShortfallAsync(
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
            .AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.Operator));

        return endpoints;
    }

    /// <summary>
    /// <c>POST /v1/orders/{orderId}/lines/{lineId}/refund-shortfall</c> 的處理邏輯
    /// （BE-34 分類表的 A20）。BE-35 從 inline lambda 抽出來（純搬移），好讓測試直接呼叫。
    /// </summary>
    /// <remarks>
    /// 跟 A3／A4 同款：<c>RefundLineShortfallAsync</c> commit 之後退款事件已經送出，
    /// 底層是品項狀態守衛，所以「組回應失敗 → abandon → 重試」<b>回不了成功</b>。
    /// BE-35 改用 <see cref="BffHttp.ExecuteIdempotentAsync{TState, TResponse}"/> 兩階段多載，
    /// <c>M1aEndpoints.ToAdminOrderAsync</c> 落在不會失敗的 <c>render</c> 那一段。
    /// </remarks>
    internal static async Task<IResult> RefundLineShortfallAsync(
        string orderId,
        string lineId,
        M1aEndpoints.CancelOrderInput input,
        HttpContext context,
        IOrderingApplication ordering,
        ICustomerDirectory customers,
        IPaymentQuery payments,
        ICatalogQuery catalog,
        IIdempotencyStore idempotency,
        ILogger logger,
        CancellationToken cancellationToken)
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
            token => ordering.RefundLineShortfallAsync(
                new OrderId(parsedOrder),
                new OrderLineId(parsedLine),
                input.Reason,
                input.RefundTo,
                token),
            (order, token) => M1aEndpoints.ToAdminOrderAsync(
                order,
                customers,
                payments,
                catalog,
                logger,
                token),
            StatusCodes.Status200OK,
            cancellationToken);
    }
}
