namespace Daigou.Platform.Abstractions.Messaging;

/// <summary>
/// 事件發布。<b>實作必須把訊息寫進 platform.outbox_message，
/// 且與呼叫端的業務寫入在同一個資料庫交易裡</b>——要嘛都成功，要嘛都不發生。
/// </summary>
/// <remarks>
/// 這是模組間非同步溝通可靠的唯一基礎。
/// 禁止實作成「直接呼叫訂閱者」或「丟進 Valkey」：前者破壞交易邊界，後者會掉訊息。
/// </remarks>
public interface IEventPublisher
{
    /// <summary>把事件排入 outbox。這個方法<b>不</b>負責派送，派送由 Worker 做。</summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;
}

/// <summary>
/// 模組的工作單元。一次 SaveChanges 同時寫入業務資料與 outbox。
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
