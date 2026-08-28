namespace GreyGray.Platform.Abstractions.Idempotency;

/// <summary>
/// 對外 API 與第三方 webhook 的重放防護，對應 <c>platform.idempotency_key</c>。
/// </summary>
/// <remarks>
/// 兩處一定要用：
/// <list type="bullet">
///   <item>客人送出訂單（可能連點兩次）——所有寫入型 API 接受 <c>Idempotency-Key</c> header。</item>
///   <item>金流商 webhook（綠界會重送）——驗簽 ＋ 時戳容忍窗 ＋ event id 去重，三者缺一不可。</item>
/// </list>
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// 嘗試取得這個 key 的處理權。
    /// 回傳 <see cref="IdempotencyOutcome.AlreadyCompleted"/> 時，<paramref name="cachedResponse"/> 是原始結果，
    /// 呼叫端<b>直接回傳它</b>，不要重跑業務邏輯。
    /// </summary>
    Task<(IdempotencyOutcome Outcome, string? cachedResponse)> TryBeginAsync(
        string key,
        string scope,
        string requestHash,
        CancellationToken cancellationToken);

    Task CompleteAsync(string key, string scope, string responseSnapshot, CancellationToken cancellationToken);

    Task AbandonAsync(string key, string scope, CancellationToken cancellationToken);
}

public enum IdempotencyOutcome
{
    /// <summary>第一次看到這個 key，可以往下做。</summary>
    Proceed,

    /// <summary>同 key 已完成過，回傳快取結果。</summary>
    AlreadyCompleted,

    /// <summary>同 key 正在處理中（另一個請求還沒結束）。回 409。</summary>
    InFlight,

    /// <summary>同 key 但 request 內容不同——這是用戶端的錯，回 422。</summary>
    KeyReusedWithDifferentPayload,
}
