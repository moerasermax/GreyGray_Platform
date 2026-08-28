using Daigou.Shared.Kernel;

namespace Daigou.Platform.Outbox;

/// <summary>
/// 對應 <c>platform.outbox_message</c>。欄位定義見 db/migrations/0001_platform.sql。
/// </summary>
/// <remarks>
/// <b>TenantId 現在就要存</b>：這張表一旦上線帶了資料，之後要補欄位還得回填歷史訊息。
/// </remarks>
public sealed class OutboxMessage
{
    public required Guid Id { get; init; }

    public required TenantId TenantId { get; init; }

    /// <summary>發出事件的聚合型別，例如 <c>Order</c>。用來查「這張單發過哪些事件」。</summary>
    public required string AggregateType { get; init; }

    public required string AggregateId { get; init; }

    /// <summary>穩定的事件型別名，例如 <c>ordering.OrderPlaced.v1</c>。</summary>
    public required string EventType { get; init; }

    /// <summary>事件內容，jsonb。</summary>
    public required string Payload { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string CorrelationId { get; init; }

    public string? CausationId { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    /// <summary>超過重試上限後為 true，並發告警。人工處理完才清掉。</summary>
    public bool IsDeadLettered { get; set; }
}

/// <summary>
/// Outbox 派送器的行為契約。實作要點（見 docs/06-狀態機與Saga.md 圖 15）：
/// <list type="number">
///   <item>用 <c>SELECT ... FOR UPDATE SKIP LOCKED</c> 取批次，讓多實例可安全並行。</item>
///   <item>派送前先 <c>SET LOCAL app.tenant_id</c>（<b>LOCAL</b>，不是 SET——
///         pgBouncer transaction pooling 會讓 session 變數跨交易洩漏）。</item>
///   <item>失敗則 attempts+1、next_attempt_at 指數退避；超過上限進死信並告警。</item>
///   <item>整個 Worker 進程用 Postgres advisory lock 互斥，避免部署時新舊兩份同時派送。</item>
/// </list>
/// </summary>
public interface IOutboxDispatcher
{
    Task<int> DispatchBatchAsync(int batchSize, CancellationToken cancellationToken);
}
