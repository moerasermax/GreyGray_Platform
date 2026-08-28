using GreyGray.Shared.Kernel;

namespace GreyGray.Platform.Abstractions.Messaging;

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

    /// <summary>
    /// 發出這個事件的聚合型別，例如 <c>Order</c>、<c>Campaign</c>。
    /// 對應 <c>platform.outbox_message.aggregate_type</c>（NOT NULL）。
    /// </summary>
    /// <remarks>
    /// 查問題時第一個會用到的就是「這張單發過哪些事件」，靠的是
    /// <c>(aggregate_type, aggregate_id)</c> 這組索引。所以它不是可選的診斷欄位，
    /// 而是事件契約的一部分——由事件自己宣告，不由呼叫端填，避免同一種事件在不同呼叫點填出不同的值。
    /// </remarks>
    string AggregateType { get; }

    /// <summary>
    /// 聚合實例的識別，例如訂單編號。對應 <c>platform.outbox_message.aggregate_id</c>（NOT NULL）。
    /// </summary>
    string AggregateId { get; }

    /// <summary>穩定的事件型別名，例如 <c>ordering.OrderPlaced.v1</c>。改名等於破壞契約。</summary>
    static abstract string EventType { get; }
}

/// <summary>
/// 整合事件的共同欄位。各模組的事件 record 繼承這個。
/// </summary>
/// <remarks>
/// <see cref="AggregateType"/> 與 <see cref="AggregateId"/> 宣告成 <c>abstract</c>，
/// 目的是讓<b>編譯器</b>逼每個事件把它們填掉——這兩欄在 <c>platform.outbox_message</c> 是 NOT NULL，
/// 漏填的後果是執行期 INSERT 失敗，而那時已經在交易裡了。
/// </remarks>
public abstract record IntegrationEventBase(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId)
{
    /// <inheritdoc cref="IIntegrationEvent.AggregateType" />
    public abstract string AggregateType { get; }

    /// <inheritdoc cref="IIntegrationEvent.AggregateId" />
    public abstract string AggregateId { get; }
}

/// <summary>
/// 事件消費者。同一個事件可以有多個 handler，各自獨立重試、獨立進死信。
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
