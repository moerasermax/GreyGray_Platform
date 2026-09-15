using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;

namespace GreyGray.Api.Storefront.Logistics;

internal static class CvsLogisticsEndpoints
{
    internal const string SelectionCacheKeyPrefix = "logistics:cvs-selection:";
    private const string SelectionAlphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SelectedLifetime = TimeSpan.FromMinutes(60);
    private static readonly JsonSerializerOptions CacheJson = new(JsonSerializerDefaults.Web);

    public static void Map(RouteGroupBuilder api)
    {
        var services = ((IEndpointRouteBuilder)api).ServiceProvider;
        var configuration = services.GetRequiredService<IConfiguration>();
        var settings = ReadSettings(configuration);
        var logger = services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GreyGray.Api.Storefront.Logistics");

        api.MapPost("/logistics/cvs-map-sessions", async (
            CvsMapSessionInput input,
            HttpContext context,
            IDistributedCache cache,
            IClock clock,
            CancellationToken cancellationToken) =>
            await CreateMapSessionAsync(
                input,
                context,
                configuration,
                settings,
                cache,
                clock,
                cancellationToken))
            .Accepts<CvsMapSessionInput>("application/json")
            .Produces<CvsMapSessionResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

        api.MapPost("/logistics/cvs-map/reply", async (
            HttpContext context,
            IDistributedCache cache,
            IClock clock,
            CancellationToken cancellationToken) =>
            await HandleMapReplyAsync(
                context,
                configuration,
                settings,
                cache,
                clock,
                logger,
                cancellationToken))
            .Accepts<IFormCollection>("application/x-www-form-urlencoded")
            .Produces(StatusCodes.Status303SeeOther);

        api.MapGet("/logistics/cvs-selections/{selectionId}", async (
            string selectionId,
            HttpContext context,
            IDistributedCache cache,
            IClock clock,
            CancellationToken cancellationToken) =>
            await GetSelectionAsync(
                selectionId,
                context,
                cache,
                clock,
                cancellationToken))
            .Produces<CvsStoreSelectionResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");
    }

    internal static CvsMapSettings? ReadSettings(IConfiguration configuration)
    {
        var merchantId = configuration["Logistics:ECPay:MerchantId"]?.Trim();
        if (string.IsNullOrWhiteSpace(merchantId))
        {
            return null;
        }

        var logisticsSubType = configuration["Logistics:ECPay:LogisticsSubType"]?.Trim();
        logisticsSubType = string.IsNullOrWhiteSpace(logisticsSubType)
            ? "UNIMARTC2C"
            : logisticsSubType;
        if (!StringComparer.Ordinal.Equals(logisticsSubType, "UNIMARTC2C"))
        {
            throw new InvalidOperationException(
                $"設定 'Logistics:ECPay:LogisticsSubType' 的值 '{logisticsSubType}' 不受支援。"
                + "目前只支援 7-ELEVEN C2C（UNIMARTC2C）。");
        }

        var mapUrlText = configuration["Logistics:ECPay:MapUrl"];
        if (string.IsNullOrWhiteSpace(mapUrlText)
            || !Uri.TryCreate(mapUrlText.Trim(), UriKind.Absolute, out var mapUrl))
        {
            throw new InvalidOperationException(
                "缺少或無效的設定 'Logistics:ECPay:MapUrl'；有 MerchantId 時必須提供絕對網址。"
                + "不設預設值，避免正式帳號安靜地送到測試站。");
        }

        var allowNonEcpay = configuration.GetValue(
            "Logistics:ECPay:AllowNonEcpayEndpoints",
            false);
        if (!allowNonEcpay
            && (mapUrl.Scheme != Uri.UriSchemeHttps || !IsEcpayHost(mapUrl.Host)))
        {
            throw new InvalidOperationException(
                $"設定 'Logistics:ECPay:MapUrl' 的值 '{mapUrl}' 不是綠界 https 網址。"
                + "正式環境必須使用 ecpay.com.tw 或其子網域；dev 模擬器請明確設定 "
                + "'Logistics:ECPay:AllowNonEcpayEndpoints=true'。");
        }

        return new CvsMapSettings(merchantId, logisticsSubType, mapUrl);
    }

    internal static async Task<IResult> CreateMapSessionAsync(
        CvsMapSessionInput input,
        HttpContext context,
        IConfiguration configuration,
        CvsMapSettings? settings,
        IDistributedCache cache,
        IClock clock,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (settings is null)
        {
            return BffHttp.Problem(
                new Error("logistics.not-configured", "這台伺服器尚未設定超商物流。"),
                StatusCodes.Status503ServiceUnavailable);
        }

        var serverReplyUrl = StorefrontUrls.BuildPublicApiUrl(
            configuration,
            context.Request,
            "/v1/logistics/cvs-map/reply",
            "綠界電子地圖要用它組出 /v1/logistics/cvs-map/reply。"
        ).ToString();
        var cartId = M1aEndpoints.GetOrCreateCartId(context);
        var selectionId = RandomNumberGenerator.GetString(SelectionAlphabet, 20);
        var createdAt = clock.UtcNow;
        var expiresAt = createdAt.Add(PendingLifetime);
        var ticket = new CvsSelectionTicket(
            cartId.Value,
            CvsSelectionStatus.Pending,
            createdAt,
            expiresAt,
            null,
            null,
            null,
            false);
        await SetTicketAsync(cache, selectionId, ticket, cancellationToken);

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MerchantID"] = settings.MerchantId,
            ["LogisticsType"] = "CVS",
            ["LogisticsSubType"] = settings.LogisticsSubType,
            ["IsCollection"] = "N",
            ["ServerReplyURL"] = serverReplyUrl,
            ["ExtraData"] = selectionId,
        };
        if (input.Device is not null)
        {
            fields["Device"] = input.Device == CvsMapDevice.Mobile ? "1" : "0";
        }

        return Results.Ok(new CvsMapSessionResponse(
            selectionId,
            "POST",
            settings.MapUrl,
            fields,
            expiresAt));
    }

    internal static async Task<IResult> HandleMapReplyAsync(
        HttpContext context,
        IConfiguration configuration,
        CvsMapSettings? settings,
        IDistributedCache cache,
        IClock clock,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> fields;
        try
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            fields = form.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToString(),
                StringComparer.Ordinal);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsFormReadException(exception))
        {
            logger.LogWarning(exception, "綠界選店回傳的表單無法解析。");
            return RedirectFailure(configuration, "invalid-reply");
        }

        if (settings is null)
        {
            return RedirectFailure(configuration, "not-configured");
        }

        var selectionId = fields.GetValueOrDefault("ExtraData", string.Empty);
        if (!IsValidSelectionId(selectionId))
        {
            return RedirectFailure(configuration, "invalid-reply");
        }

        CvsSelectionTicket? ticket;
        try
        {
            ticket = await GetTicketAsync(cache, selectionId, cancellationToken);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "讀取選店票快取失敗。");
            return RedirectFailure(configuration, "unavailable");
        }

        if (ticket is null || clock.UtcNow >= ticket.ExpiresAt)
        {
            return RedirectFailure(configuration, "expired");
        }

        if (ticket.Status == CvsSelectionStatus.Selected)
        {
            return RedirectFailure(configuration, "already-used");
        }

        if (!StringComparer.Ordinal.Equals(
                fields.GetValueOrDefault("MerchantID", string.Empty),
                settings.MerchantId)
            || !StringComparer.Ordinal.Equals(
                fields.GetValueOrDefault("LogisticsSubType", string.Empty),
                settings.LogisticsSubType))
        {
            return RedirectFailure(configuration, "invalid-reply");
        }

        var storeCode = fields.GetValueOrDefault("CVSStoreID", string.Empty).Trim();
        var storeName = fields.GetValueOrDefault("CVSStoreName", string.Empty).Trim();
        var storeAddress = fields.GetValueOrDefault("CVSAddress", string.Empty).Trim();
        if (!IsAsciiAlphaNumeric(storeCode, 1, 20)
            || !IsValidText(storeName, 1, 50)
            || !IsValidText(storeAddress, 1, 200))
        {
            return RedirectFailure(configuration, "invalid-reply");
        }

        var selectedAt = clock.UtcNow;
        var selected = ticket with
        {
            Status = CvsSelectionStatus.Selected,
            ExpiresAt = selectedAt.Add(SelectedLifetime),
            StoreCode = storeCode,
            StoreName = storeName,
            StoreAddress = storeAddress,
            IsOutlying = StringComparer.Ordinal.Equals(
                fields.GetValueOrDefault("CVSOutSide", string.Empty),
                "1"),
        };

        try
        {
            // 這是刻意接受的讀後寫競態：票是 119 bits 的秘密且只在客人自己的瀏覽器裡；
            // 兩個幾乎同時的合法回傳可能都成功，後寫者會覆蓋前寫者，不為此另加分散式鎖。
            await SetTicketAsync(cache, selectionId, selected, cancellationToken);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "寫入選店票快取失敗。");
            return RedirectFailure(configuration, "unavailable");
        }

        return RedirectSuccess(configuration, selectionId);
    }

    internal static async Task<IResult> GetSelectionAsync(
        string selectionId,
        HttpContext context,
        IDistributedCache cache,
        IClock clock,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!IsValidSelectionId(selectionId)
            || !M1aEndpoints.TryGetCartId(context, out var cartId))
        {
            return SelectionNotFound();
        }

        var ticket = await GetTicketAsync(cache, selectionId, cancellationToken);
        if (!IsSelectedForCart(ticket, cartId, clock.UtcNow))
        {
            return SelectionNotFound();
        }

        return Results.Ok(ToResponse(selectionId, ticket!));
    }

    internal static async Task<Result<CvsStoreSelectionResponse>> ResolveForCheckoutAsync(
        string selectionId,
        CartId cartId,
        IDistributedCache cache,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!IsValidSelectionId(selectionId))
        {
            return SelectionExpired();
        }

        var ticket = await GetTicketAsync(cache, selectionId, cancellationToken);
        return IsSelectedForCart(ticket, cartId, clock.UtcNow)
            ? ToResponse(selectionId, ticket!)
            : SelectionExpired();
    }

    internal static bool IsValidSelectionId(string? selectionId) =>
        selectionId is not null && IsAsciiAlphaNumeric(selectionId, 20, 20);

    internal static string CacheKey(string selectionId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(selectionId));
        return SelectionCacheKeyPrefix + Convert.ToHexStringLower(hash);
    }

    internal static Task SetTicketAsync(
        IDistributedCache cache,
        string selectionId,
        CvsSelectionTicket ticket,
        CancellationToken cancellationToken) =>
        cache.SetAsync(
            CacheKey(selectionId),
            JsonSerializer.SerializeToUtf8Bytes(ticket, CacheJson),
            new DistributedCacheEntryOptions { AbsoluteExpiration = ticket.ExpiresAt },
            cancellationToken);

    internal static async Task<CvsSelectionTicket?> GetTicketAsync(
        IDistributedCache cache,
        string selectionId,
        CancellationToken cancellationToken)
    {
        var value = await cache.GetAsync(CacheKey(selectionId), cancellationToken);
        return value is null
            ? null
            : JsonSerializer.Deserialize<CvsSelectionTicket>(value, CacheJson);
    }

    private static bool IsEcpayHost(string host) =>
        host.Equals("ecpay.com.tw", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".ecpay.com.tw", StringComparison.OrdinalIgnoreCase);

    private static bool IsSelectedForCart(
        CvsSelectionTicket? ticket,
        CartId cartId,
        DateTimeOffset now) =>
        ticket is
        {
            Status: CvsSelectionStatus.Selected,
            StoreCode: not null,
            StoreName: not null,
            StoreAddress: not null,
        }
        && ticket.CartId == cartId.Value
        && now < ticket.ExpiresAt;

    private static CvsStoreSelectionResponse ToResponse(
        string selectionId,
        CvsSelectionTicket ticket) =>
        new(
            selectionId,
            ticket.StoreCode!,
            ticket.StoreName!,
            ticket.StoreAddress!,
            ticket.IsOutlying,
            ticket.ExpiresAt);

    private static Result<CvsStoreSelectionResponse> SelectionExpired() =>
        Result<CvsStoreSelectionResponse>.Failure(
            "checkout.store-selection-expired",
            "門市選擇已過期，請重新選擇門市。");

    private static IResult SelectionNotFound() =>
        BffHttp.Problem(
            new Error("logistics.selection-not-found", "找不到這筆門市選擇。"),
            StatusCodes.Status404NotFound);

    private static IResult RedirectSuccess(IConfiguration configuration, string selectionId) =>
        new SeeOtherResult(StorefrontUrls.BuildPublicUrl(
            configuration,
            $"/checkout?cvsSelection={Uri.EscapeDataString(selectionId)}",
            "綠界選店完成後要用它導回 /checkout。"
        ).ToString());

    private static IResult RedirectFailure(IConfiguration configuration, string errorCode) =>
        new SeeOtherResult(StorefrontUrls.BuildPublicUrl(
            configuration,
            $"/checkout?cvsSelectionError={Uri.EscapeDataString(errorCode)}",
            "綠界選店完成後要用它導回 /checkout。"
        ).ToString());

    private static bool IsAsciiAlphaNumeric(string value, int minLength, int maxLength) =>
        value.Length >= minLength
        && value.Length <= maxLength
        && value.All(character =>
            character is >= '0' and <= '9'
            or >= 'A' and <= 'Z'
            or >= 'a' and <= 'z');

    private static bool IsValidText(string value, int minLength, int maxLength) =>
        value.Length >= minLength
        && value.Length <= maxLength
        && value.All(character => !char.IsControl(character));

    private static bool IsFormReadException(Exception exception) =>
        exception is InvalidOperationException
            or InvalidDataException
            or IOException
            or BadHttpRequestException
            or OperationCanceledException;
}

internal sealed record CvsMapSettings(
    string MerchantId,
    string LogisticsSubType,
    Uri MapUrl);

internal enum CvsMapDevice
{
    Desktop,
    Mobile,
}

internal sealed record CvsMapSessionInput(CvsMapDevice? Device);

internal sealed record CvsMapSessionResponse(
    string SelectionId,
    string Method,
    Uri Action,
    IReadOnlyDictionary<string, string> Fields,
    DateTimeOffset ExpiresAt);

internal sealed record CvsStoreSelectionResponse(
    string SelectionId,
    string StoreCode,
    string StoreName,
    string StoreAddress,
    bool IsOutlying,
    DateTimeOffset ExpiresAt);

internal enum CvsSelectionStatus
{
    Pending,
    Selected,
}

internal sealed record CvsSelectionTicket(
    Guid CartId,
    CvsSelectionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? StoreCode,
    string? StoreName,
    string? StoreAddress,
    bool IsOutlying);

internal sealed class SeeOtherResult(string location) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
        httpContext.Response.Headers.Location = location;
        return Task.CompletedTask;
    }
}
