using Daigou.Shared.Kernel;

namespace Daigou.Platform.Abstractions.Saga;

/// <summary>
/// 長流程的時間驅動轉移，對應 <c>platform.saga_timer</c>。
/// </summary>
/// <remarks>
/// 本專案有三個時間驅動的規則，全部走這裡：
/// <list type="number">
///   <item>逾期未付款 → 建單後 N 小時自動取消、釋放需求登記、通知客人。</item>
///   <item>截團 → 出發前 N 天自動把 Campaign 從「開團中」轉「已截團」。</item>
///   <item><b>現場漲價逾時視為照買</b> → 這條讓「問客人」從阻塞式同步等待
///         降級成非阻塞的通知加軌跡記錄，你人在店裡不用站著等。</item>
/// </list>
/// Worker 以 Postgres advisory lock 確保單一實例掃描，避免多節點重複觸發。
/// </remarks>
public interface ISagaTimerScheduler
{
    /// <summary>排一個到期回呼。回傳 timer id，取消時要用。</summary>
    Task<Guid> ScheduleAsync(
        string sagaType,
        string sagaId,
        DateTimeOffset fireAt,
        string payload,
        TenantId tenantId,
        CancellationToken cancellationToken);

    /// <summary>取消尚未觸發的 timer。已觸發的取消是 no-op（不是錯誤）。</summary>
    Task CancelAsync(Guid timerId, CancellationToken cancellationToken);

    /// <summary>取消某個 saga 實例底下全部未觸發的 timer。訂單取消時用。</summary>
    Task CancelAllForSagaAsync(string sagaType, string sagaId, CancellationToken cancellationToken);
}

/// <summary>Timer 到期時被呼叫。實作必須冪等——timer 可能被重複觸發。</summary>
public interface ISagaTimeoutHandler
{
    /// <summary>這個 handler 負責哪個 sagaType。</summary>
    static abstract string SagaType { get; }

    Task HandleTimeoutAsync(string sagaId, string payload, CancellationToken cancellationToken);
}
