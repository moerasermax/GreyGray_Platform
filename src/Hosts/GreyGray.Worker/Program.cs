using GreyGray.Modules.Catalog.Infra;
using GreyGray.Modules.Identity.Infra;
using GreyGray.Platform.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Worker — Outbox dispatcher、Saga timer、排程、報表產生。
//
// 這是唯一會處理 Outbox 與 Saga Timer 的行程。
// 用 Postgres advisory lock 確保單一實例——避免部署時新舊兩份同時跑造成事件重複派送。
// 沒有 listener，不開任何 port。

GreyGrayTelemetry.ConfigureW3CActivityIds();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddGreyGrayRuntimeContext();
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("GreyGray.Worker"))
    .WithTracing(tracing => tracing
        .AddSource(GreyGrayTelemetry.ActivitySourceName)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());

// TODO(M0-1..3)：AddPlatform() —— 含 OutboxDispatcherService 與 SagaTimerService。
builder.Services
    .AddIdentityModule(builder.Configuration)
    .AddCatalogModule(builder.Configuration);

// TODO(M0-5)：Identity／Catalog 樣板驗收後，逐一複製到其餘模組；
//             Worker 最終需要全部模組的事件 handler。
// TODO(M3-6)：Cloudflare Queues consumer —— 拉取 webhook 緩衝層的訊息。

var host = builder.Build();
await host.RunAsync();
