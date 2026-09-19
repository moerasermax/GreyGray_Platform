using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using GreyGray.Modules.CustomerService.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;

// M1aEndpoints.cs 已經有一份同類宣告，但那個檔不在 BE-55 的 allow 裡（BE-54 同波在改）；
// 這個模組自己的測試需要碰 internal 型別，放在自己擁有的檔案裡宣告。
[assembly: InternalsVisibleTo("GreyGray.CustomerService.Tests")]

namespace GreyGray.Api.Storefront;

/// <summary>
/// 客服工單——前台匿名留言端點（ADR-040）。獨立檔，<b>不改 <see cref="M1aEndpoints"/></b>：
/// BE-54 同一波在改那個檔，兩包同時動同一個檔一定撞。
/// </summary>
internal static class SupportEndpoints
{
    private const string SessionCookie = "gg_session";

    /// <summary>同一 IP／同一購物車 cookie 每小時上限（ADR-040）。客服留言不是安全邊界，
    /// 這個數字是防灌的粗略門檻，不是精確配額。</summary>
    private const int RateLimitPerHour = 20;

    public static IEndpointRouteBuilder MapSupportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/v1");
        var logger = endpoints.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GreyGray.Api.Storefront.SupportEndpoints");

        api.MapPost("/support/tickets", async (
            CreateSupportTicketInput input,
            HttpContext context,
            ISessionStore sessions,
            ICustomerServiceTickets tickets,
            IIdempotencyStore idempotency,
            IDistributedCache cache,
            CancellationToken cancellationToken) =>
            await CreateTicketAsync(
                input,
                context,
                sessions,
                tickets,
                idempotency,
                cache,
                logger,
                cancellationToken))
            .Accepts<CreateSupportTicketInput>("application/json")
            .Produces<SupportTicket>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json");

        return endpoints;
    }

    internal static async Task<IResult> CreateTicketAsync(
        CreateSupportTicketInput input,
        HttpContext context,
        ISessionStore sessions,
        ICustomerServiceTickets tickets,
        IIdempotencyStore idempotency,
        IDistributedCache cache,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var cartId = M1aEndpoints.GetOrCreateCartId(context);
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var allowed = await TryConsumeRateLimitAsync(cache, ip, cartId.ToString(), logger, cancellationToken);
        if (!allowed)
        {
            return BffHttp.Problem(
                new Error("support.too-many-requests", "留言太頻繁，請稍後再試。"),
                StatusCodes.Status429TooManyRequests);
        }

        var customerId = await GetOptionalCustomerIdAsync(context, sessions, cancellationToken);
        var request = new CreateSupportTicketRequest(
            input.Message,
            input.ContactEmail,
            input.ContactPhone,
            input.MenuPath ?? [],
            input.OrderId,
            customerId);

        return await BffHttp.ExecuteIdempotentAsync(
            context,
            idempotency,
            "storefront:support:create-ticket",
            request,
            async token =>
            {
                var result = await tickets.CreateAsync(request, token);
                return result.IsFailure
                    ? Result<SupportTicket>.Failure(result.Error)
                    : result.Value;
            },
            static (ticket, _) => Task.FromResult(ticket),
            StatusCodes.Status201Created,
            cancellationToken);
    }

    private static async Task<CustomerId?> GetOptionalCustomerIdAsync(
        HttpContext context,
        ISessionStore sessions,
        CancellationToken cancellationToken)
    {
        var session = await BffHttp.GetSessionAsync(
            context,
            sessions,
            SessionCookie,
            SessionSubjectKind.Customer,
            cancellationToken);
        return session is not null && Guid.TryParseExact(session.SubjectId, "N", out var id)
            ? new CustomerId(id)
            : null;
    }

    /// <summary>
    /// ⚠ <c>IDistributedCache</c>（Garnet／<c>AddStackExchangeRedisCache</c>）斷線會拋例外——
    /// <b>fail-open</b>：捕捉到就記 error log 並放行，不讓 KV 掛掉連正常留言都收不了。
    /// </summary>
    private static async Task<bool> TryConsumeRateLimitAsync(
        IDistributedCache cache,
        string ip,
        string cartId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var key = RateLimitCacheKey(ip, cartId);
        try
        {
            var raw = await cache.GetAsync(key, cancellationToken);
            var count = raw is { Length: > 0 }
                && int.TryParse(
                    Encoding.UTF8.GetString(raw),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                ? parsed
                : 0;
            if (count >= RateLimitPerHour)
            {
                return false;
            }

            await cache.SetAsync(
                key,
                Encoding.UTF8.GetBytes((count + 1).ToString(CultureInfo.InvariantCulture)),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) },
                cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "客服留言防灌計數讀寫失敗，fail-open 放行。");
            return true;
        }
    }

    internal static string RateLimitCacheKey(string ip, string cartId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ip + "|" + cartId));
        return "support:rate:" + Convert.ToHexStringLower(hash);
    }
}

internal sealed record CreateSupportTicketInput(
    string Message,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<string>? MenuPath,
    TicketOrderId? OrderId);
