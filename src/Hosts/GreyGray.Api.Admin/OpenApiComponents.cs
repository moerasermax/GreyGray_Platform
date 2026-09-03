using System.Net.Http;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GreyGray.Api.Admin;

/// <summary>
/// 補上凍結契約（<c>docs/api/openapi.admin.yaml</c>）要求、但 AddOpenApi() 不會自動產生的共用元件。
///
/// Idempotency-Key 是端點從 <c>HttpContext.Request.Headers</c> 讀的，不是繫結參數，
/// source generator 抓不到；cursor/limit 雖然有繫結，但預設會產生行內 schema，
/// 凍結契約要求的是 <c>$ref</c> 指到 components/parameters。
/// 這裡只補宣告——所有端點的實際行為（讀 header、400、雜湊比對……）本來就是對的。
/// </summary>
internal sealed class M1aOpenApiComponents : IOpenApiDocumentTransformer
{
    /// <summary>需要 Idempotency-Key 的端點（動詞、路徑）。與凍結契約逐一比對過。</summary>
    private static readonly HashSet<(HttpMethod Method, string Path)> IdempotentEndpoints =
    [
        (HttpMethod.Post, "/v1/auth/login"),
        (HttpMethod.Post, "/v1/auth/logout"),
        (HttpMethod.Post, "/v1/categories"),
        (HttpMethod.Patch, "/v1/categories/{categoryId}"),
        (HttpMethod.Post, "/v1/products"),
        (HttpMethod.Patch, "/v1/products/{productId}"),
        (HttpMethod.Post, "/v1/products/{productId}/skus"),
        (HttpMethod.Patch, "/v1/skus/{skuId}"),
        (HttpMethod.Post, "/v1/campaigns"),
        (HttpMethod.Patch, "/v1/campaigns/{campaignId}"),
        (HttpMethod.Post, "/v1/campaigns/{campaignId}/publish"),
        (HttpMethod.Post, "/v1/campaigns/{campaignId}/close"),
        (HttpMethod.Post, "/v1/campaigns/{campaignId}/cancel"),
        (HttpMethod.Post, "/v1/campaigns/{campaignId}/settle"),
        (HttpMethod.Post, "/v1/campaigns/{campaignId}/offers"),
        (HttpMethod.Delete, "/v1/campaigns/{campaignId}/offers/{offerId}"),
        (HttpMethod.Post, "/v1/orders/{orderId}/cancel"),
        (HttpMethod.Post, "/v1/orders/{orderId}/lines/{lineId}/cancel"),
        (HttpMethod.Post, "/v1/orders/{orderId}/lines/{lineId}/refund-shortfall"),
    ];

    /// <summary>用游標分頁（cursor／limit）的清單端點。</summary>
    private static readonly HashSet<(HttpMethod Method, string Path)> PagedListEndpoints =
    [
        (HttpMethod.Get, "/v1/products"),
        (HttpMethod.Get, "/v1/campaigns"),
        (HttpMethod.Get, "/v1/orders"),
        (HttpMethod.Get, "/v1/ledger/entries"),
    ];

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Parameters ??= new Dictionary<string, IOpenApiParameter>();

        document.Components.Parameters["IdempotencyKey"] = new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 255 },
        };
        document.Components.Parameters["Cursor"] = new OpenApiParameter
        {
            Name = "cursor",
            In = ParameterLocation.Query,
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
