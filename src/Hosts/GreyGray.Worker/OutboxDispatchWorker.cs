using GreyGray.Platform.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GreyGray.Worker;

/// <summary>以短輪詢驅動 PostgreSQL outbox；每批使用獨立 DI scope。</summary>
internal sealed class OutboxDispatchWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxDispatchWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
                var processed = await dispatcher.DispatchBatchAsync(100, stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox 派送批次失敗，{Delay} 後重試。", FailureDelay);
                await Task.Delay(FailureDelay, stoppingToken);
            }
        }
    }
}
