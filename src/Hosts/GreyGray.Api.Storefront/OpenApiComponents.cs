using System.Net.Http;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GreyGray.Api.Storefront;

/// <summary>
/// 補上凍結契約（<c>docs/api/openapi.storefront.yaml</c>）要求、但 AddOpenApi() 不會自動產生的共用元件。
///
/// Idempotency-Key 是端點從 <c>HttpContext.Request.Headers</c> 讀的，不是繫結參數，
/// source generator 抓不到；cursor/limit 雖然有繫結，但預設會產生行內 schema，
/// 凍結契約要求的是 <c>$ref</c> 指到 components/parameters。
/// 這裡只補宣告——所有端點的實際行為（讀 header、400、雜湊比對……）本來就是對的。
/// </summary>
internal sealed class M1aOpenApiComponents : IOpenApiDocumentTransformer
{
    private const string IdempotencyKeyDescription =
        "同一個使用者動作用同一把 key，重試時不變。詳見 `docs/05-API契約.md` §4。\n";

    private const string CursorDescription = "上一頁回傳的 `nextCursor`。第一頁不帶。";

    private const string SessionCookieDescription =
        "gg_session=<opaque>; HttpOnly; Secure; SameSite=Lax; Path=/; Max-Age=2592000";

    /// <summary>需要 Idempotency-Key 的端點（動詞、路徑）。與凍結契約逐一比對過。</summary>
    private static readonly HashSet<(HttpMethod Method, string Path)> IdempotentEndpoints =
    [
        (HttpMethod.Post, "/v1/auth/register"),
        (HttpMethod.Post, "/v1/auth/login"),
        (HttpMethod.Post, "/v1/auth/logout"),
        (HttpMethod.Patch, "/v1/me"),
        (HttpMethod.Post, "/v1/me/addresses"),
        (HttpMethod.Put, "/v1/me/addresses/{addressId}"),
        (HttpMethod.Delete, "/v1/me/addresses/{addressId}"),
        (HttpMethod.Put, "/v1/me/favorites/{productId}"),
        (HttpMethod.Delete, "/v1/me/favorites/{productId}"),
        (HttpMethod.Post, "/v1/cart/lines"),
        (HttpMethod.Patch, "/v1/cart/lines/{lineId}"),
        (HttpMethod.Delete, "/v1/cart/lines/{lineId}"),
        (HttpMethod.Post, "/v1/cart/quote"),
        (HttpMethod.Post, "/v1/cart/checkout"),
        (HttpMethod.Post, "/v1/orders/{orderId}/cancel"),
        (HttpMethod.Post, "/v1/orders/{orderId}/payment"),
    ];

    /// <summary>用游標分頁（cursor／limit）的清單端點。</summary>
    private static readonly HashSet<(HttpMethod Method, string Path)> PagedListEndpoints =
    [
        (HttpMethod.Get, "/v1/products"),
        (HttpMethod.Get, "/v1/campaigns"),
        (HttpMethod.Get, "/v1/orders"),
        (HttpMethod.Get, "/v1/me/favorites"),
    ];

    /// <summary>登入成功會回 <c>Set-Cookie</c> 的端點與對應成功狀態碼。</summary>
    private static readonly Dictionary<(HttpMethod Method, string Path), string> SessionCookieResponses = new()
    {
        [(HttpMethod.Post, "/v1/auth/register")] = "201",
        [(HttpMethod.Post, "/v1/auth/login")] = "200",
    };

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Parameters ??= new Dictionary<string, IOpenApiParameter>();
        document.Components.Headers ??= new Dictionary<string, IOpenApiHeader>();

        document.Components.Parameters["IdempotencyKey"] = new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = true,
            Description = IdempotencyKeyDescription,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 255 },
        };
        document.Components.Parameters["Cursor"] = new OpenApiParameter
        {
            Name = "cursor",
            In = ParameterLocation.Query,
            Description = CursorDescription,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        };
        document.Components.Parameters["Limit"] = new OpenApiParameter
        {
            Name = "limit",
            In = ParameterLocation.Query,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Minimum = "1",
                Maximum = "100",
                Default = JsonValue.Create(20),
            },
        };
        document.Components.Headers["SessionCookie"] = new OpenApiHeader
        {
            Description = SessionCookieDescription,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        };

        if (document.Paths is not null)
        {
            foreach (var (path, pathItem) in document.Paths)
            {
                if (pathItem is null)
                {
                    continue;
                }

                foreach (var (method, operation) in pathItem.Operations ?? [])
                {
                    if (operation is null)
                    {
                        continue;
                    }

                    var key = (method, path);
                    if (IdempotentEndpoints.Contains(key))
                    {
                        operation.Parameters ??= new List<IOpenApiParameter>();
                        operation.Parameters.Add(new OpenApiParameterReference("IdempotencyKey", document));
                    }

                    if (PagedListEndpoints.Contains(key))
                    {
                        ReplaceWithReference(operation, "cursor", "Cursor", document);
                        ReplaceWithReference(operation, "limit", "Limit", document);
                    }

                    if (SessionCookieResponses.TryGetValue(key, out var status) &&
                        operation.Responses is not null &&
                        operation.Responses.TryGetValue(status, out var response) &&
                        response is OpenApiResponse concreteResponse)
                    {
                        concreteResponse.Headers ??= new Dictionary<string, IOpenApiHeader>();
                        concreteResponse.Headers["Set-Cookie"] = new OpenApiHeaderReference("SessionCookie", document);
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    private static void ReplaceWithReference(
        OpenApiOperation operation,
        string parameterName,
        string componentName,
        OpenApiDocument document)
    {
        if (operation.Parameters is null)
        {
            return;
        }

        for (var index = 0; index < operation.Parameters.Count; index++)
        {
            if (string.Equals(operation.Parameters[index].Name, parameterName, StringComparison.Ordinal))
            {
                operation.Parameters[index] = new OpenApiParameterReference(componentName, document);
                return;
            }
        }
    }
}
