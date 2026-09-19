using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Shared.Kernel;
using Microsoft.Extensions.Caching.Distributed;

namespace GreyGray.CustomerService.Tests;

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public DateOnly TodayInTaipei => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}

internal sealed class FakeCorrelationContext(TenantId? tenantId = null) : ICorrelationContext
{
    public string CorrelationId => "test-correlation";

    public string? CausationId => null;

    public TenantId TenantId { get; } = tenantId ?? TenantId.Default;
}

/// <summary>什麼都不做的工作單元——搭配純記憶體 repository 時 SaveChanges 沒有意義。</summary>
internal sealed class NoopUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}

/// <summary>
/// 記憶體版冪等儲存，語意比照 <c>IdempotencyStore</c>：同一個 request hash 再來一次拿到
/// <c>Proceed</c>；<c>ABANDONED</c> 之後同一把 key 也可以重跑。
/// </summary>
internal sealed class InspectableIdempotencyStore : IIdempotencyStore
{
    private readonly Dictionary<(string Key, string Scope), Entry> _entries = [];

    public Task<(IdempotencyOutcome Outcome, string? cachedResponse)> TryBeginAsync(
        string key,
        string scope,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var identity = (key, scope);
        if (!_entries.TryGetValue(identity, out var entry))
        {
            _entries[identity] = new Entry(requestHash, Status.InFlight, null);
            return Task.FromResult((IdempotencyOutcome.Proceed, (string?)null));
        }

        if (!StringComparer.Ordinal.Equals(entry.RequestHash, requestHash))
        {
            return Task.FromResult((IdempotencyOutcome.KeyReusedWithDifferentPayload, (string?)null));
        }

        return entry.State switch
        {
            Status.Abandoned => Restart(identity, entry),
            Status.Completed => Task.FromResult((IdempotencyOutcome.AlreadyCompleted, entry.Response)),
            _ => Task.FromResult((IdempotencyOutcome.InFlight, (string?)null)),
        };

        Task<(IdempotencyOutcome, string?)> Restart((string, string) id, Entry current)
        {
            _entries[id] = current with { State = Status.InFlight, Response = null };
            return Task.FromResult((IdempotencyOutcome.Proceed, (string?)null));
        }
    }

    public Task CompleteAsync(string key, string scope, string responseSnapshot, CancellationToken cancellationToken)
    {
        var identity = (key, scope);
        if (_entries.TryGetValue(identity, out var entry))
        {
            _entries[identity] = entry with { State = Status.Completed, Response = responseSnapshot };
        }

        return Task.CompletedTask;
    }

    public Task AbandonAsync(string key, string scope, CancellationToken cancellationToken)
    {
        var identity = (key, scope);
        if (_entries.TryGetValue(identity, out var entry))
        {
            _entries[identity] = entry with { State = Status.Abandoned };
        }

        return Task.CompletedTask;
    }

    private enum Status
    {
        InFlight,
        Completed,
        Abandoned,
    }

    private sealed record Entry(string RequestHash, Status State, string? Response);
}

/// <summary>純記憶體 <see cref="ISessionStore"/>，只用來讓端點測試模擬已登入客人／員工。</summary>
internal sealed class FakeSessionStore : ISessionStore
{
    private readonly Dictionary<string, SessionRecord> _sessions = [];

    public string Issue(SessionSubjectKind kind, string subjectId, string? role = null)
    {
        var token = Guid.NewGuid().ToString("N");
        _sessions[token] = new SessionRecord(
            subjectId, kind, TenantId.Default, role, DateTimeOffset.UtcNow, DateTimeOffset.MaxValue);
        return token;
    }

    public Task<string> CreateAsync(
        string subjectId,
        SessionSubjectKind subjectKind,
        TenantId tenantId,
        string? role,
        CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        _sessions[token] = new SessionRecord(
            subjectId, subjectKind, tenantId, role, DateTimeOffset.UtcNow, DateTimeOffset.MaxValue);
        return Task.FromResult(token);
    }

    public Task<SessionRecord?> GetAsync(string token, CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.GetValueOrDefault(token));

    public Task DeleteAsync(string token, CancellationToken cancellationToken)
    {
        _sessions.Remove(token);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 可控的 <see cref="IDistributedCache"/>——<see cref="ThrowOnGet"/>／<see cref="ThrowOnSet"/>
/// 用來驗證 fail-open：KV 斷線時留言仍然收得下來。
/// </summary>
internal sealed class InspectableDistributedCache : IDistributedCache
{
    private readonly Dictionary<string, byte[]> _entries = [];

    public bool ThrowOnGet { get; set; }

    public bool ThrowOnSet { get; set; }

    public byte[]? Get(string key) => _entries.GetValueOrDefault(key);

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        if (ThrowOnGet)
        {
            throw new InvalidOperationException("模擬 Garnet 斷線（Get）。");
        }

        return Task.FromResult(_entries.GetValueOrDefault(key));
    }

    public void Refresh(string key)
    {
    }

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Remove(string key) => _entries.Remove(key);

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        _entries.Remove(key);
        return Task.CompletedTask;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _entries[key] = value;

    public Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        if (ThrowOnSet)
        {
            throw new InvalidOperationException("模擬 Garnet 斷線（Set）。");
        }

        _entries[key] = value;
        return Task.CompletedTask;
    }
}
