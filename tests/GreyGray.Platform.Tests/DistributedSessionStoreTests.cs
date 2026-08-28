using System.Collections.Concurrent;
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Platform.Sessions;
using GreyGray.Shared.Kernel;
using Microsoft.Extensions.Caching.Distributed;
using Shouldly;
using Xunit;

namespace GreyGray.Platform.Tests;

public sealed class DistributedSessionStoreTests
{
    [Fact(DisplayName = "Session cookie 只拿 opaque token，伺服器端保存完整身分")]
    public async Task Creates_opaque_token_and_round_trips_server_side_session()
    {
        var cache = new RecordingDistributedCache();
        var clock = new AdjustableClock(
            new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero));
        var store = new DistributedSessionStore(cache, clock);

        var token = await store.CreateAsync(
            "customer-1",
            SessionSubjectKind.Customer,
            TenantId.Default,
            null,
            TestContext.Current.CancellationToken);

        token.Length.ShouldBe(64);
        token.All(Uri.IsHexDigit).ShouldBeTrue();
        cache.Keys.Single().ShouldNotContain(token);

        var session = await store.GetAsync(
            token,
            TestContext.Current.CancellationToken);
        session.ShouldNotBeNull();
        session.SubjectId.ShouldBe("customer-1");
        session.SubjectKind.ShouldBe(SessionSubjectKind.Customer);
        session.TenantId.ShouldBe(TenantId.Default);
        session.ExpiresAt.ShouldBe(clock.UtcNow.AddDays(30));
    }

    [Fact(DisplayName = "過期或格式錯誤的 session fail closed")]
    public async Task Rejects_expired_and_malformed_tokens()
    {
        var cache = new RecordingDistributedCache();
        var clock = new AdjustableClock(
            new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero));
        var store = new DistributedSessionStore(cache, clock);
        var token = await store.CreateAsync(
            "staff-1",
            SessionSubjectKind.Staff,
            TenantId.Default,
            "Owner",
            TestContext.Current.CancellationToken);

        (await store.GetAsync(
            "not-a-token",
            TestContext.Current.CancellationToken)).ShouldBeNull();

        clock.Advance(TimeSpan.FromDays(31));
        (await store.GetAsync(
            token,
            TestContext.Current.CancellationToken)).ShouldBeNull();
        cache.Keys.ShouldBeEmpty();
    }
}

internal sealed class RecordingDistributedCache : IDistributedCache
{
    private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => _entries.Keys.ToArray();

    public byte[]? Get(string key) =>
        _entries.TryGetValue(key, out var value) ? value.ToArray() : null;

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
        Task.FromResult(Get(key));

    public void Refresh(string key)
    {
    }

    public Task RefreshAsync(string key, CancellationToken token = default) =>
        Task.CompletedTask;

    public void Remove(string key) => _entries.TryRemove(key, out _);

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        _entries[key] = value.ToArray();

    public Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }
}
