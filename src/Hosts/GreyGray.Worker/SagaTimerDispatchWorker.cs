using GreyGray.Platform.Saga;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GreyGray.Worker;

/// <summary>以獨立 scope 與 advisory lock 1002 掃描到期的 saga timer。</summary>
internal sealed class SagaTimerDispatchWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SagaTimerDispatchWorker> logger) : BackgroundService
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
                var dispatcher = scope.ServiceProvider.GetRequiredService<SagaTimerDispatcher>();
                var processed = await dispatcher.DispatchDueAsync(100, stoppingToken);
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
                logger.LogError(exception, "Saga timer 派送批次失敗，{Delay} 後重試。", FailureDelay);
                await Task.Delay(FailureDelay, stoppingToken);
            }
        }
    }
}
