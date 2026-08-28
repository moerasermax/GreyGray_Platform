using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Shared.Kernel;
using GreyGray.Shared.Kernel.Json;
using Microsoft.AspNetCore.Http;

namespace GreyGray.Platform.Http;

public static class BffHttp
{
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
