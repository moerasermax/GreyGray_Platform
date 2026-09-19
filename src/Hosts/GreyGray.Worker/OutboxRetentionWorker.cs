using GreyGray.Platform.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GreyGray.Worker;

/// <summary>
/// 依保存期限清掉已投遞的 outbox 訊息（#57）。比照 <see cref="OutboxDispatchWorker"/>：
/// 每批一個獨立 DI scope，例外只記 log 不讓行程倒下。
/// </summary>
/// <remarks>
/// 掃描間隔固定一小時——保存期限以「天」為單位，沒有必要更密集；
/// 但每一批仍設上限，避免第一次上線時一次刪掉累積已久的整張表而長時間持鎖。
/// </remarks>
internal sealed class OutboxRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxRetentionWorker> logger) : BackgroundService
{
    private const int BatchSize = 500;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<OutboxRetentionSweeper>();

                // 一輪掃到刪不動為止：滿批代表還有積欠，接著刪；不滿批就是清乾淨了。
                int deleted;
                do
                {
                    deleted = await sweeper.PurgeExpiredAsync(BatchSize, stoppingToken);
                }
                while (deleted == BatchSize && !stoppingToken.IsCancellationRequested);

                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox 保存期限清理失敗，{Delay} 後重試。", FailureDelay);
                await Task.Delay(FailureDelay, stoppingToken);
            }
        }
    }
}
