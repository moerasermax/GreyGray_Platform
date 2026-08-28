using Daigou.Shared.Kernel;

namespace Daigou.Platform.Abstractions.Messaging;

/// <summary>
/// 模組之間的整合事件。<b>只承載「已發生的事實 ＋ 識別碼」</b>，
/// 不承載對方模組的內部模型。需要細節就回頭呼叫對方的 Contracts 介面。
/// </summary>
/// <remarks>
/// 派送語意是 at-least-once（見 docs/06-狀態機與Saga.md）。
/// <b>所有 handler 必須冪等，這是硬性契約，不是建議。</b>
/// </remarks>
public interface IIntegrationEvent
{
    /// <summary>事件唯一識別。消費端用它做去重。</summary>
    Guid EventId { get; }

    /// <summary>事實發生的時間（不是派送時間）。</summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// 租戶。<b>outbox 訊息必須自帶 tenant_id</b>——dispatcher 是背景程序，
    /// 不在任何請求裡，沒有使用者上下文可推。這是多租戶最容易漏、
    /// 漏了會靜默寫到錯租戶的一條（見 docs/00-decisions.md ADR-006）。
    /// </summary>
    TenantId TenantId { get; }

    /// <summary>穩定的事件型別名，例如 <c>ordering.OrderPlaced.v1</c>。改名等於破壞契約。</summary>
    static abstract string EventType { get; }
}

/// <summary>整合事件的共同欄位。各模組的事件 record 繼承這個。</summary>
public abstract record IntegrationEventBase(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId);

/// <summary>
/// 事件消費者。同一個事件可以有多個 handler，各自獨立重試、獨立進死信。
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
