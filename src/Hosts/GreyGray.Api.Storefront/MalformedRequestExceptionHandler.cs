using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Diagnostics;

namespace GreyGray.Api.Storefront;

/// <summary>
/// 把「請求本身壞掉」翻成契約規定的 <c>400</c> ＋ <c>application/problem+json</c>（#37 附帶）。
/// </summary>
/// <remarks>
/// <para>
/// minimal API 先綁定 request body 再進 lambda，綁不動時丟
/// <see cref="BadHttpRequestException"/>（自帶 <c>StatusCode = 400</c>）。修這一包之前：
/// Development 被 <c>UseExceptionHandler</c> 當成一般例外 → <b>500</b>；
/// 非 Development 的 <c>RouteHandlerOptions.ThrowOnBadRequest</c> 預設 false → <b>空 body 的 400</b>，
/// 沒有 <c>code</c>、沒有 problem+json。兩個都違反 <c>docs/05-API契約.md</c> §「狀態碼」：
/// 可預期的失敗一律 <c>404</c>／<c>409</c>／<c>422</c>，<b>不會</b>是 500；請求格式錯誤是 <c>400</c>。
/// </para>
/// <para>
/// 兩個環境行為要一致，所以 <c>Program.cs</c> 明確把 <c>ThrowOnBadRequest</c> 設成 true，
/// 一律走到這裡。回應用 <see cref="BffHttp.Problem"/> 組，跟其他錯誤同一個形狀。
/// </para>
/// <para>
/// <b>訊息刻意寫死。</b>框架給的 <c>exception.Message</c> 長這樣：
/// 「Failed to read parameter &quot;CompleteCheckoutInput input&quot; from the request body as JSON.」——
/// 內部型別名不准出現在回應裡（<c>docs/05</c> §10 第 3 條）。細節留在 log 與 trace。
/// </para>
/// </remarks>
internal sealed class MalformedRequestExceptionHandler(
    ILogger<MalformedRequestExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        logger.LogInformation(
            badRequest,
            "請求格式錯誤：{Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);
        await BffHttp
            .Problem(
                new Error("platform.malformed-request", "請求內容格式不正確。"),
                badRequest.StatusCode)
            .ExecuteAsync(httpContext);
        return true;
    }
}
