using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GreyGray.Platform.Messaging;

/// <summary>
/// 用 <c>platform.processed_message</c> 包裝整合事件 handler，讓資料庫副作用具備消費端冪等性。
/// </summary>
/// <remarks>
/// 一個 decorator instance 應與 <typeparamref name="TDbContext"/> 使用相同 DI scope。
/// handler 內所有經由該 DbContext 寫入的副作用，會與 processed marker 一起提交或一起回滾。
/// </remarks>
public sealed class IdempotentIntegrationEventHandler<TEvent, THandler, TDbContext>(
    THandler innerHandler,
    TDbContext dbContext,
    IClock clock) : IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent
    where THandler : class, IIntegrationEventHandler<TEvent>
    where TDbContext : DbContext
{
    private static readonly string HandlerName =
        typeof(THandler).FullName ?? typeof(THandler).Name;

    public async Task HandleAsync(TEvent @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(IdempotentIntegrationEventHandler<TEvent, THandler, TDbContext>)} " +
                "必須擁有模組 DbContext 的交易，不能包在呼叫端既有交易內。");
        }

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        try
        {
            // Dispatcher 的 PlatformDbContext 與模組 DbContext 使用不同連線；模組交易也必須
            // 自行設定 transaction-local tenant，不能依賴另一條連線上的 session state。
            // set_config(..., true) 等價於 SET LOCAL，且 tenant id 維持 bind parameter。
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT set_config(
                    'app.tenant_id',
                    {@event.TenantId.Value.ToString("D")},
                    true)
                """, cancellationToken);

            var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO platform.processed_message (event_id, handler_name, processed_at)
                VALUES ({@event.EventId}, {HandlerName}, {clock.UtcNow})
                ON CONFLICT (event_id, handler_name) DO NOTHING
                """, cancellationToken);

            if (inserted == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            await innerHandler.HandleAsync(@event, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // 即使原例外是取消，也要完成 rollback，否則 marker 可能留在未完成交易中。
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }
}

/// <summary>消費端冪等 handler 的 DI 登錄捷徑。</summary>
public static class IdempotentIntegrationEventHandlerServiceCollectionExtensions
{
    /// <summary>
    /// 將 <typeparamref name="THandler"/> 登錄為帶 processed-message 保護的
    /// <see cref="IIntegrationEventHandler{TEvent}"/>。
    /// </summary>
    public static IServiceCollection AddIdempotentIntegrationEventHandler<
        TEvent,
        THandler,
        TDbContext>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<THandler>();
        services.AddScoped<IIntegrationEventHandler<TEvent>>(serviceProvider =>
            new IdempotentIntegrationEventHandler<TEvent, THandler, TDbContext>(
                serviceProvider.GetRequiredService<THandler>(),
                serviceProvider.GetRequiredService<TDbContext>(),
                serviceProvider.GetRequiredService<IClock>()));

        return services;
    }
}
