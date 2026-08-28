using GreyGray.Worker;
using GreyGray.Modules.Catalog.Infra;
using GreyGray.Modules.Identity.Infra;
using GreyGray.Modules.Notification.Infra;
using GreyGray.Platform;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Observability;
using GreyGray.Platform.Outbox;
using GreyGray.Platform.Saga;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

var platformConnectionString = builder.Configuration.GetConnectionString("GreyGray_platform");
if (string.IsNullOrWhiteSpace(platformConnectionString))
{
    throw new InvalidOperationException(
        "缺少 Worker 資料庫連線字串 'ConnectionStrings:GreyGray_platform'。");
}

builder.Services.AddDbContext<PlatformDbContext>(options =>
    options.UseNpgsql(platformConnectionString));
builder.Services.TryAddSingleton<EventTypeRegistry>();
builder.Services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();
builder.Services.AddScoped<SagaTimerDispatcher>();
builder.Services.AddHostedService<OutboxDispatchWorker>();
builder.Services.AddHostedService<SagaTimerDispatchWorker>();

builder.Services
    .AddIdentityModule(builder.Configuration)
    .AddCatalogModule(builder.Configuration)
    .AddNotificationModule(builder.Configuration);

// TODO(M0-5)：逐一複製到其餘模組；Worker 最終需要全部模組的 handler。
// TODO(M3-6)：Cloudflare Queues consumer —— 拉取 webhook 緩衝層的訊息。

var host = builder.Build();

// 組態錯誤必須讓 Worker 啟動失敗，不能被背景輪詢的重試迴圈吞掉。
_ = host.Services.GetRequiredService<EventTypeRegistry>();
await using (var validationScope = host.Services.CreateAsyncScope())
{
    _ = validationScope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
    _ = validationScope.ServiceProvider.GetRequiredService<SagaTimerDispatcher>();
}

await host.RunAsync();
