using System.Diagnostics;
using GreyGray.Api.Storefront;
using GreyGray.Modules.Catalog.Infra;
using GreyGray.Modules.Identity.Infra;
using GreyGray.Platform.Observability;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Storefront BFF — 公開 API，給客人用（Web / LINE LIFF）。
//
// 這一層只做「組裝與授權」，不含任何業務邏輯（藍圖 §07）。
// 前面擋著 Cloudflare WAF ＋ Turnstile ＋ Rate Limit，自架端零 inbound port（走 cloudflared）。
//
// M0 的驗收條件之一：服務重開機後自動起來。部署以 NSSM 註冊成 Windows service，
// 沿用既有 12 個服務的 S4U ＋ BootTrigger ＋ 每 5 分鐘 watchdog 慣例。

GreyGrayTelemetry.ConfigureW3CActivityIds();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddGreyGrayRuntimeContext();
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("GreyGray.Api.Storefront"))
    .WithTracing(tracing => tracing
        .AddSource(GreyGrayTelemetry.ActivitySourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());

// TODO(M0-1..3)：AddPlatform() —— Outbox、Idempotency、Saga Timer。
builder.Services
    .AddIdentityModule(builder.Configuration)
    .AddCatalogModule(builder.Configuration);

// TODO(M0-5)：Identity／Catalog 樣板驗收後，逐一複製到其餘模組。
//             只能呼叫 *.Infra 公開的註冊擴充方法，不得 using 任何 *.Core 命名空間。
// TODO(M1a-1)：BFF 認證 —— access token 永不進瀏覽器，
//              前端只拿 HttpOnly; Secure; SameSite=Lax cookie。

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapM0CustomerEndpoints();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// ASP.NET Core 會從請求的 W3C traceparent 建立 Activity；回應把目前的 server span 傳回客戶端。
app.Use(async (context, next) =>
{
    Activity? fallbackActivity = null;
    if (Activity.Current is null)
    {
        fallbackActivity = new Activity("GreyGray.Api.Storefront request").Start();
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

// 健康檢查。prod-monitor 會收錄這個端點（port 5000，5000 號段避開既有占用）。
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "storefront" }));

app.Run();
