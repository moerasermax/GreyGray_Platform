using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Worker — Outbox dispatcher、Saga timer、排程、報表產生。
//
// 這是唯一會處理 Outbox 與 Saga Timer 的行程。
// 用 Postgres advisory lock 確保單一實例——避免部署時新舊兩份同時跑造成事件重複派送。
// 沒有 listener，不開任何 port。

var builder = Host.CreateApplicationBuilder(args);

// TODO(M0-4)：AddPlatform() —— 含 OutboxDispatcherService 與 SagaTimerService。
// TODO(M0-5)：逐一 AddXxxModule() —— Worker 需要全部模組的事件 handler。
// TODO(M3-6)：Cloudflare Queues consumer —— 拉取 webhook 緩衝層的訊息。

var host = builder.Build();
await host.RunAsync();
