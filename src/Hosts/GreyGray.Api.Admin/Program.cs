using System.Diagnostics;
using GreyGray.Api.Admin;
using GreyGray.Modules.Campaign.Infra;
using GreyGray.Modules.Catalog.Infra;
using GreyGray.Modules.Checkout.Infra;
using GreyGray.Modules.Fulfillment.Infra;
using GreyGray.Modules.Identity.Infra;
using GreyGray.Modules.Inventory.Infra;
using GreyGray.Modules.Ledger.Infra;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Payment.Infra;
using GreyGray.Modules.Pricing.Infra;
using GreyGray.Modules.Procurement.Infra;
using GreyGray.Platform;
using GreyGray.Platform.Http;
using GreyGray.Platform.Observability;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Diagnostics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Admin BFF — 內部 API，給你、太太與團隊用。
//
// 完全不對公網開放：身分由 Cloudflare Access（Zero Trust）驗過並簽發 JWT，
// 後端驗 CF 公鑰。團隊成員離職時在 CF 移除即可，不必改 code、不必重新部署。
//
// 權限模型與客人端完全不共用，獨立 OpenAPI 文件、獨立部署、獨立版本線。

GreyGrayTelemetry.ConfigureW3CActivityIds();

var builder = WebApplication.CreateBuilder(args);

var adminFrontendOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray() ?? [];
if (adminFrontendOrigins.Length == 0 && builder.Environment.IsDevelopment())
{
    adminFrontendOrigins = ["http://localhost:5003", "http://127.0.0.1:5003"];
}

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<M1aOpenApiComponents>());
builder.Services.AddProblemDetails();

// 跟 Storefront 同一組接線（#37 附帶）：請求體壞掉時兩個環境都回
// 400 ＋ application/problem+json（platform.malformed-request），不是 Development 500、
// Production 空 body 400。理由與細節見 Storefront 的 MalformedRequestExceptionHandler.cs。
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddExceptionHandler<MalformedRequestExceptionHandler>();
if (adminFrontendOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy(
        "AdminFrontend",
        policy => policy
            .WithOrigins(adminFrontendOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));
}
builder.Services.ConfigureHttpJsonOptions(options =>
    BffHttp.ApplyGreyGrayJson(options.SerializerOptions));
builder.Services.AddGreyGrayRuntimeContext();
builder.Services.AddGreyGrayApiPlatform(builder.Configuration);
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("GreyGray.Api.Admin"))
    .WithTracing(tracing => tracing
        .AddSource(GreyGrayTelemetry.ActivitySourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());

// TODO(M0-1..3)：AddPlatform() —— Outbox、Idempotency、Saga Timer。
builder.Services
    .AddIdentityModule(builder.Configuration)
    .AddCatalogModule(builder.Configuration)
    .AddCampaignModule(builder.Configuration)
    .AddPricingModule(builder.Configuration)
    .AddInventoryModule(builder.Configuration)
    .AddCheckoutModule(builder.Configuration)
    .AddOrderingModule(builder.Configuration)
    .AddProcurementModule(builder.Configuration)
    .AddPaymentModule(builder.Configuration)
    .AddFulfillmentModule(builder.Configuration)
    .AddLedgerModule(builder.Configuration);

// TODO(M0-5)：Identity／Catalog 樣板驗收後，逐一複製到其餘模組。
// TODO(M1a-2)：Cloudflare Access JWT 驗證 —— 驗 CF 公鑰、比對 aud，
//              解析失敗一律拒絕，不得 fallback 成匿名。

var app = builder.Build();

if (adminFrontendOrigins.Length > 0)
{
    app.UseCors("AdminFrontend");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapM1aAdminEndpoints();
app.MapM1bProcurementEndpoints();
app.MapM1bCompensationEndpoints();
app.MapM1bShortfallRefundEndpoints();
app.MapM1bFulfillmentEndpoints();
app.MapM2InventoryEndpoints();

app.UseExceptionHandler();
app.UseStatusCodePages();

// ASP.NET Core 會從請求的 W3C traceparent 建立 Activity；回應把目前的 server span 傳回客戶端。
app.Use(async (context, next) =>
{
    Activity? fallbackActivity = null;
    if (Activity.Current is null)
    {
        fallbackActivity = new Activity("GreyGray.Api.Admin request").Start();
    }

    try
    {
        string? responseTraceParent = Activity.Current?.Id;
        context.Response.OnStarting(() =>
        {
            if (responseTraceParent is not null)
            {
                context.Response.Headers["traceparent"] = responseTraceParent;
            }

            return Task.CompletedTask;
        });

        await next(context);
    }
    finally
    {
        fallbackActivity?.Stop();
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "admin" }));

app.Run();

namespace GreyGray.Api.Admin
{
    /// <summary>
    /// 把「請求本身壞掉」翻成契約規定的 <c>400</c> ＋ <c>application/problem+json</c>。
    /// </summary>
    /// <remarks>
    /// 這是 Storefront <c>MalformedRequestExceptionHandler.cs</c> 的第二份——兩個 Host 各自
    /// 獨立部署、不互相參考，而共同的家（<c>src/Platform/Http/</c>）不在 BE-41 的授權範圍內。
    /// <b>改一邊就要改另一邊。</b>詳細理由寫在 Storefront 那一份。
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
}
