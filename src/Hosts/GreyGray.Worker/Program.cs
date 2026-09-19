using GreyGray.Worker;
using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Platform;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Observability;
using GreyGray.Platform.Outbox;
using GreyGray.Platform.Saga;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

// #57：outbox 投遞成功後原本永遠不刪。ADR-039 把收件人真實姓名與手機放進事件 payload，
// 所以保存期限是那個決定的前提條件，不是順手加的功能。天數做成組態（比照 ADR-025）。
builder.Services.AddSingleton(OutboxRetentionPolicy.FromDays(
    builder.Configuration.GetValue("Platform:Outbox:RetentionDays", OutboxRetentionPolicy.DefaultDays)));
builder.Services.AddScoped<OutboxRetentionSweeper>();

builder.Services.AddHostedService<OutboxDispatchWorker>();
builder.Services.AddHostedService<SagaTimerDispatchWorker>();
builder.Services.AddHostedService<OutboxRetentionWorker>();

// 模組清單只有一份，在 WorkerModules.AddWorkerModules；
// 架構測試 WorkerCompositionTests 呼叫的是同一個方法，所以它驗到的組合
// 就是這個行程真的跑的組合（#41 的成因正是清單只寫在這裡、沒有人測）。
builder.Services.AddWorkerModules(builder.Configuration);

// TODO(M3-6)：Cloudflare Queues consumer —— 拉取 webhook 緩衝層的訊息。

// 開機驗證用：這個行程登記了哪些整合事件 handler。
// 一定要在 Build() 之前收集——builder.Services 建完就不該再碰；
// 真正的解析在下面的 validation scope 裡做（handler 全是 scoped）。
var integrationEventHandlerTypes = builder.Services
    .Select(descriptor => descriptor.ServiceType)
    .Where(static type => type.IsConstructedGenericType
        && type.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>))
    .Distinct()
    .ToArray();

var host = builder.Build();

// 組態錯誤必須讓 Worker 啟動失敗，不能被背景輪詢的重試迴圈吞掉。
_ = host.Services.GetRequiredService<EventTypeRegistry>();
await using (var validationScope = host.Services.CreateAsyncScope())
{
    _ = validationScope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
    _ = validationScope.ServiceProvider.GetRequiredService<SagaTimerDispatcher>();
    _ = validationScope.ServiceProvider.GetRequiredService<OutboxRetentionSweeper>();

    // 「查了零個對象」跟「查過都沒事」不能長得一樣：Worker 一定有登記 handler，
    // 收到空清單代表這段驗證失去意義（例如上面的篩選條件被改壞）。
    if (integrationEventHandlerTypes.Length == 0)
    {
        throw new InvalidOperationException(
            "開機驗證沒有找到任何 IIntegrationEventHandler<T> 登記——" +
            "Worker 是唯一派送 Outbox 的行程，這不可能是對的。");
    }

    // #41：每一個登記的事件 handler 都要在開機時就解得出相依。
    // 沒有這一段的話，缺模組只會在 outbox 真的派送到那則事件時才炸，
    // 而那時只看得到重試失敗的 log，訂單則永遠停在原狀態。
    //
    // ★ 這一圈也涵蓋 Payment 的 handler，而它們解析時會走到 IEcpayGateway／EcpaySettings
    //   （Payment.Infra/ModuleRegistration.cs 的 ReadEcpaySettings）：
    //   **Worker 跟 Storefront 一樣，沒有 Payment:ECPay:* 就不開機**，這是刻意的 fail-fast
    //   ——退款跑在 Worker（Payment.Infra/OrderingEventHandlers），設定缺了早晚要炸，
    //   炸在開機比炸在退款當下好。dev 起 Worker 要帶
    //   ops\start-dev-hosts.ps1 -UseEcpaySimulator，正式機則靠 secrets\ecpay.json
    //   （ops/deploy.ps1 會注入那三個鍵）。
    foreach (var handlerType in integrationEventHandlerTypes)
    {
        try
        {
            var handlers = validationScope.ServiceProvider.GetServices(handlerType);
            if (!handlers.Any())
            {
                throw new InvalidOperationException("解析結果是空的。");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"事件 handler {handlerType} 的相依在開機時解不出來；" +
                "Worker 拒絕啟動，以免事件派送時才失敗。", ex);
        }
    }

    // 點名驗 Fulfillment：Ordering 的 ShipmentDeliveredHandler 對 IFulfillmentQuery 是
    // Lazy 相依（Ordering.Infra/ModuleRegistration.cs 的工廠委派），所以上面那圈
    // 「解析 handler」在缺 Fulfillment 模組時仍然全數成功——#41 就是這樣漏掉的。
    // 光解析 handler 抓不到，只能點名。
    _ = validationScope.ServiceProvider.GetRequiredService<IFulfillmentQuery>();
}

await host.RunAsync();
