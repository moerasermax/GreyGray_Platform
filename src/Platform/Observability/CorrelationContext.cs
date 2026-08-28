using System.Diagnostics;
using GreyGray.Shared.Kernel;

namespace GreyGray.Platform.Observability;

/// <summary>
/// 以目前的 <see cref="Activity"/> 為主要來源，並允許背景工作逐則訊息覆寫的關聯內容。
/// </summary>
/// <remarks>
/// 覆寫值透過 <see cref="AsyncLocal{T}"/> 隨非同步流程傳遞，不會在同時處理的訊息之間共用。
/// API 的 W3C <c>traceparent</c> 由 ASP.NET Core 建立成 <see cref="Activity.Current"/>；
/// Worker 則在派送每一則 outbox 訊息時呼叫 <see cref="BeginScope"/>。
/// </remarks>
public sealed class CorrelationContext : ICorrelationContext
{
    private readonly AsyncLocal<ScopeState?> _currentScope = new();
    private readonly AsyncLocal<FallbackState?> _fallback = new();

    /// <inheritdoc />
    public string CorrelationId => _currentScope.Value?.CorrelationId
        ?? Activity.Current?.TraceId.ToHexString()
        ?? GetFallback().CorrelationId;

    /// <inheritdoc />
    public string? CausationId => Activity.Current?.SpanId.ToHexString()
        ?? _currentScope.Value?.CausationId
        ?? GetFallback().CausationId;

    /// <inheritdoc />
    public TenantId TenantId => _currentScope.Value?.TenantId ?? TenantId.Default;

    /// <summary>
    /// 在目前的非同步流程覆寫關聯內容。Dispatcher 必須為每一則 outbox 訊息各開一個 scope。
    /// </summary>
    /// <param name="correlationId">W3C TraceId，32 位小寫十六進位。</param>
    /// <param name="causationId">產生訊息的 SpanId，16 位小寫十六進位；沒有已知父 span 時可為 null。</param>
    /// <param name="tenantId">訊息明確攜帶的租戶識別。</param>
    public IDisposable BeginScope(string correlationId, string? causationId, TenantId tenantId)
    {
        GreyGrayTelemetry.ValidateTraceId(correlationId);

        if (causationId is not null)
        {
            GreyGrayTelemetry.ValidateSpanId(causationId);
        }

        ScopeState? previous = _currentScope.Value;
        var current = new ScopeState(correlationId, causationId, tenantId);
        _currentScope.Value = current;

        return new Scope(this, current, previous);
    }

    private FallbackState GetFallback()
    {
        FallbackState? fallback = _fallback.Value;
        if (fallback is not null)
        {
            return fallback;
        }

        fallback = new FallbackState(
            ActivityTraceId.CreateRandom().ToHexString(),
            ActivitySpanId.CreateRandom().ToHexString());
        _fallback.Value = fallback;
        return fallback;
    }

    private sealed record ScopeState(
        string CorrelationId,
        string? CausationId,
        TenantId TenantId);

    private sealed record FallbackState(string CorrelationId, string CausationId);

    private sealed class Scope(
        CorrelationContext owner,
        ScopeState current,
        ScopeState? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (ReferenceEquals(owner._currentScope.Value, current))
            {
                owner._currentScope.Value = previous;
            }
        }
    }
}
