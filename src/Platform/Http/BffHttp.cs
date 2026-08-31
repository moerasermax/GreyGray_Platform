using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Shared.Kernel;
using GreyGray.Shared.Kernel.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GreyGray.Platform.Http;

public static class BffHttp
{
    /// <summary>
    /// 端點層退化訊息的 log 類別名稱。<see cref="BffHttp"/> 是 static class，
    /// 不能當 <c>ILogger&lt;T&gt;</c> 的型別引數，所以用固定字串。
    /// </summary>
    private const string LogCategory = "GreyGray.Platform.Http.BffHttp";

    /// <summary>
    /// <c>render</c> 丟例外時存進冪等鍵的退化回應。<b>刻意是 JSON <c>null</c> 而不是 <c>{}</c></b>：
    /// 這條路只有在「組回應」本身出 bug 時才走得到，此時不存在正確的回應可以存；
    /// <c>{}</c> 會被前端當成一個所有欄位都缺席的正常物件而靜默錯下去，<c>null</c> 會立刻炸開。
    /// 副作用已經產生，所以無論如何都不能 abandon，只能挑一個「不會被誤認為正確」的值。
    /// </summary>
    private const string DegradedRenderSnapshot = "null";

    public static void ApplyGreyGrayJson(JsonSerializerOptions target)
    {
        target.PropertyNamingPolicy = GreyGrayJson.Options.PropertyNamingPolicy;
        target.PropertyNameCaseInsensitive = GreyGrayJson.Options.PropertyNameCaseInsensitive;
        target.DefaultIgnoreCondition = GreyGrayJson.Options.DefaultIgnoreCondition;
        target.NumberHandling = GreyGrayJson.Options.NumberHandling;
        target.ReadCommentHandling = GreyGrayJson.Options.ReadCommentHandling;
        target.AllowTrailingCommas = GreyGrayJson.Options.AllowTrailingCommas;
        target.Encoder = GreyGrayJson.Options.Encoder;
        foreach (var converter in GreyGrayJson.Options.Converters)
        {
            target.Converters.Add(converter);
        }
    }

    public static void SetSessionCookie(
        HttpResponse response,
        string cookieName,
        string token)
    {
        response.Cookies.Append(cookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromDays(30),
            IsEssential = true,
        });
    }

    public static void ClearSessionCookie(HttpResponse response, string cookieName)
    {
        response.Cookies.Delete(cookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
    }

    public static async Task<SessionRecord?> GetSessionAsync(
        HttpContext context,
        ISessionStore sessions,
        string cookieName,
        SessionSubjectKind expectedKind,
        CancellationToken cancellationToken)
    {
        if (!context.Request.Cookies.TryGetValue(cookieName, out var token) ||
            string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var session = await sessions.GetAsync(token, cancellationToken);
        return session?.SubjectKind == expectedKind ? session : null;
    }

    public static async Task DeleteSessionAsync(
        HttpContext context,
        ISessionStore sessions,
        string cookieName,
        CancellationToken cancellationToken)
    {
        if (context.Request.Cookies.TryGetValue(cookieName, out var token) &&
            !string.IsNullOrWhiteSpace(token))
        {
            await sessions.DeleteAsync(token, cancellationToken);
        }

        ClearSessionCookie(context.Response, cookieName);
    }

    public static IResult Problem(Error error, int? statusCode = null)
    {
        var status = statusCode ?? StatusFor(error.Code);
        return Results.Json(
            new
            {
                type = $"https://greygray.tw/errors/{error.Code}",
                title = error.Message,
                status,
                detail = (string?)null,
                instance = (string?)null,
                code = error.Code,
                traceId = Activity.Current?.Id,
                errors = (object?)null,
            },
            GreyGrayJson.Options,
            "application/problem+json",
            status);
    }

    public static IResult Unauthorized() => Problem(
        new Error("auth.unauthorized", "請先登入。"),
        StatusCodes.Status401Unauthorized);

    public static IResult Forbidden() => Problem(
        new Error("auth.forbidden", "你沒有執行這項操作的權限。"),
        StatusCodes.Status403Forbidden);

    public static async Task<IResult> ExecuteIdempotentAsync<T>(
        HttpContext context,
        IIdempotencyStore idempotency,
        string scope,
        object? request,
        Func<CancellationToken, Task<Result<T>>> action,
        int successStatus,
        CancellationToken cancellationToken,
        Func<T, CancellationToken, Task>? onReplay = null)
    {
        var identity = await BeginAsync(
            context,
            idempotency,
            scope,
            request,
            successStatus,
            cancellationToken);
        if (identity.EarlyResult is not null)
        {
            return identity.EarlyResult;
        }

        if (identity.CachedResponse is not null)
        {
            var cachedValue = JsonSerializer.Deserialize<T>(
                identity.CachedResponse,
                GreyGrayJson.Options) ?? throw new InvalidOperationException(
                $"冪等 scope '{scope}' 的快取無法還原成 {typeof(T).FullName}。");
            if (onReplay is not null)
            {
                await onReplay(cachedValue, cancellationToken);
            }

            return Results.Text(
                identity.CachedResponse,
                "application/json",
                Encoding.UTF8,
                successStatus);
        }

        try
        {
            var result = await action(cancellationToken);
            if (result.IsFailure)
            {
                await idempotency.AbandonAsync(identity.Key!, scope, cancellationToken);
                return Problem(result.Error);
            }

            var snapshot = JsonSerializer.Serialize(result.Value, GreyGrayJson.Options);
            await idempotency.CompleteAsync(
                identity.Key!,
                scope,
                snapshot,
                cancellationToken);
            return Results.Text(snapshot, "application/json", Encoding.UTF8, successStatus);
        }
        catch
        {
            await idempotency.AbandonAsync(identity.Key!, scope, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// 兩階段冪等執行：<paramref name="work"/> 負責「做事」（會失敗），
    /// <paramref name="render"/> 負責「組回應」（不會失敗）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// BE-35 為「現在卡在哪 #22」那一整個家族新增：<b>副作用已經 commit，之後才在組回應那一步
    /// 失敗，於是冪等鍵被 abandon、客人拿到錯誤</b>。四條語意：
    /// </para>
    /// <list type="number">
    /// <item><paramref name="work"/> 回 <c>IsFailure</c> → 照舊 <c>AbandonAsync</c> ＋ 回 Problem。
    /// 這是對的：失敗發生在還沒 commit、或 commit 了但底層可重入，重試是安全的。</item>
    /// <item><paramref name="work"/> 成功 → 副作用已經產生，<b>從這一刻起絕對不准 Abandon</b>。</item>
    /// <item><paramref name="render"/> 的回傳型別是 <typeparamref name="TResponse"/>，
    /// <b>不是</b> <c>Result&lt;TResponse&gt;</c>——「組回應失敗」在型別上就表達不出來。
    /// <b>這是這個多載存在的理由</b>：不靠人記得標記「副作用已產生」，靠型別讓錯的寫法編不過。</item>
    /// <item><paramref name="render"/> 若真的丟例外（那是 bug，不是業務失敗）→ 仍要
    /// <c>CompleteAsync</c> 把冪等鍵收掉（存 <see cref="DegradedRenderSnapshot"/>）再往上丟，
    /// <b>不准 Abandon</b>。</item>
    /// </list>
    /// <para>
    /// <b>舊多載沒有被 BE-35 改動</b>：33 個呼叫點裡有 28 個的 lambda 沒有「已 commit 之後才會
    /// 失敗」的區間，繼續用舊多載是正確的（分類依據見 <c>.dispatch/reports/BE-34.md</c>）。
    /// </para>
    /// </remarks>
    /// <typeparam name="TState">
    /// <paramref name="work"/> 產出的領域結果，原封不動交給 <paramref name="render"/>。
    /// </typeparam>
    /// <typeparam name="TResponse">回應 DTO；序列化之後就是冪等鍵存下來的快照。</typeparam>
    public static async Task<IResult> ExecuteIdempotentAsync<TState, TResponse>(
        HttpContext context,
        IIdempotencyStore idempotency,
        string scope,
        object? request,
        Func<CancellationToken, Task<Result<TState>>> work,
        Func<TState, CancellationToken, Task<TResponse>> render,
        int successStatus,
        CancellationToken cancellationToken)
    {
        var identity = await BeginAsync(
            context,
            idempotency,
            scope,
            request,
            successStatus,
            cancellationToken);
        if (identity.EarlyResult is not null)
        {
            return identity.EarlyResult;
        }

        if (identity.CachedResponse is not null)
        {
            return Results.Text(
                identity.CachedResponse,
                "application/json",
                Encoding.UTF8,
                successStatus);
        }

        TState state;
        try
        {
            var outcome = await work(cancellationToken);
            if (outcome.IsFailure)
            {
                // ① 副作用還沒產生（或底層可重入）：abandon，讓同一把 key 的重試走得回來。
                await idempotency.AbandonAsync(identity.Key!, scope, cancellationToken);
                return Problem(outcome.Error);
            }

            state = outcome.Value;
        }
        catch
        {
            await idempotency.AbandonAsync(identity.Key!, scope, CancellationToken.None);
            throw;
        }

        // ② 過了這一行，副作用已經產生。以下沒有任何一條路徑可以 AbandonAsync。
        string snapshot;
        try
        {
            snapshot = JsonSerializer.Serialize(
                await render(state, cancellationToken),
                GreyGrayJson.Options);
        }
        catch (Exception exception)
        {
            // ④ 組回應丟例外：副作用留著，冪等鍵仍然收成 COMPLETED，再把例外往上丟。
            Logger(context).LogCritical(
                exception,
                "冪等 scope {Scope} 的副作用已經產生，但組回應失敗；冪等鍵存退化回應收尾，不 abandon。",
                scope);
            await CompleteDegradedAsync(context, idempotency, identity.Key!, scope);
            throw;
        }

        // CompleteAsync 自己失敗時「不」接手：冪等鍵留在 IN_FLIGHT，重送在 lease 到期前會拿到
        // 409，到期後才會重跑。那一邊是安全的——abandon 才會讓已經產生的副作用被重做一次。
        await idempotency.CompleteAsync(identity.Key!, scope, snapshot, cancellationToken);
        return Results.Text(snapshot, "application/json", Encoding.UTF8, successStatus);
    }

    public static async Task<IResult> ExecuteIdempotentAsync(
        HttpContext context,
        IIdempotencyStore idempotency,
        string scope,
        object? request,
        Func<CancellationToken, Task<Result>> action,
        CancellationToken cancellationToken)
    {
        var identity = await BeginAsync(
            context,
            idempotency,
            scope,
            request,
            StatusCodes.Status204NoContent,
            cancellationToken);
        if (identity.EarlyResult is not null)
        {
            return identity.EarlyResult;
        }

        try
        {
            var result = await action(cancellationToken);
            if (result.IsFailure)
            {
                await idempotency.AbandonAsync(identity.Key!, scope, cancellationToken);
                return Problem(result.Error);
            }

            await idempotency.CompleteAsync(identity.Key!, scope, "{}", cancellationToken);
            return Results.NoContent();
        }
        catch
        {
            await idempotency.AbandonAsync(identity.Key!, scope, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// 把冪等鍵收成 <c>COMPLETED</c> 並存下退化回應。<b>收尾本身失敗只記錄、不往上丟</b>——
    /// 呼叫端正拿著一個要往上丟的原始例外，這裡再丟會把真正的原因蓋掉。
    /// </summary>
    private static async Task CompleteDegradedAsync(
        HttpContext context,
        IIdempotencyStore idempotency,
        string key,
        string scope)
    {
        try
        {
            await idempotency.CompleteAsync(
                key,
                scope,
                DegradedRenderSnapshot,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            Logger(context).LogCritical(
                exception,
                "冪等 scope {Scope} 的退化回應寫不回去，冪等鍵維持 IN_FLIGHT。",
                scope);
        }
    }

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger(LogCategory)
            ?? NullLogger.Instance;

    private static async Task<IdempotencyBegin> BeginAsync(
        HttpContext context,
        IIdempotencyStore idempotency,
        string scope,
        object? request,
        int successStatus,
        CancellationToken cancellationToken)
    {
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var header) ||
            string.IsNullOrWhiteSpace(header))
        {
            return new(null, Problem(
                new Error("request.idempotency-key-required", "缺少 Idempotency-Key header。"),
                StatusCodes.Status400BadRequest), null);
        }

        var key = header.ToString().Trim();
        if (key.Length > 255)
        {
            return new(null, Problem(
                new Error("request.idempotency-key-too-long", "Idempotency-Key 最多 255 個字元。"),
                StatusCodes.Status400BadRequest), null);
        }

        var payload = JsonSerializer.Serialize(request, GreyGrayJson.Options);
        var requestHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var (outcome, cached) = await idempotency.TryBeginAsync(
            key,
            scope,
            requestHash,
            cancellationToken);
        return outcome switch
        {
            IdempotencyOutcome.Proceed => new(key, null, null),
            IdempotencyOutcome.AlreadyCompleted when successStatus == StatusCodes.Status204NoContent =>
                new(null, Results.NoContent(), null),
            IdempotencyOutcome.AlreadyCompleted => new(null, null, cached),
            IdempotencyOutcome.InFlight => new(null, Problem(
                new Error("request.idempotency-in-flight", "相同操作仍在處理中，請稍後重試。"),
                StatusCodes.Status409Conflict), null),
            IdempotencyOutcome.KeyReusedWithDifferentPayload => new(null, Problem(
                new Error("request.idempotency-key-reused", "同一 Idempotency-Key 不可搭配不同內容。"),
                StatusCodes.Status422UnprocessableEntity), null),
            _ => throw new InvalidOperationException($"未知冪等結果 {outcome}。"),
        };
    }

    private static int StatusFor(string code)
    {
        if (code.Contains("invalid-credentials", StringComparison.Ordinal))
        {
            return StatusCodes.Status401Unauthorized;
        }

        if (code.Contains("not-found", StringComparison.Ordinal))
        {
            return StatusCodes.Status404NotFound;
        }

        if (code.Contains("already-paid", StringComparison.Ordinal) ||
            code.Contains("cancelled", StringComparison.Ordinal))
        {
            return StatusCodes.Status409Conflict;
        }

        return StatusCodes.Status422UnprocessableEntity;
    }

    private sealed record IdempotencyBegin(
        string? Key,
        IResult? EarlyResult,
        string? CachedResponse);
}
