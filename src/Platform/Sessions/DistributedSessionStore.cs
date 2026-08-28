using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Shared.Kernel;
using GreyGray.Shared.Kernel.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace GreyGray.Platform.Sessions;

public sealed class DistributedSessionStore(
    IDistributedCache cache,
    IClock clock) : ISessionStore
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
    private const string CacheKeyPrefix = "greygray:session:";

    public async Task<string> CreateAsync(
        string subjectId,
        SessionSubjectKind subjectKind,
        TenantId tenantId,
        string? role,
        CancellationToken cancellationToken = default)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToHexStringLower(tokenBytes);
        var issuedAt = clock.UtcNow;
        var session = new SessionRecord(
            subjectId,
            subjectKind,
            tenantId,
            role,
            issuedAt,
            issuedAt.Add(SessionLifetime));

        await cache.SetStringAsync(
            GetCacheKey(token),
            JsonSerializer.Serialize(session, GreyGrayJson.Options),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = session.ExpiresAt,
            },
            cancellationToken);

        return token;
    }

    public async Task<SessionRecord?> GetAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidToken(token))
        {
            return null;
        }

        var key = GetCacheKey(token);
        var json = await cache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        SessionRecord? session;
        try
        {
            session = JsonSerializer.Deserialize<SessionRecord>(json, GreyGrayJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (session is null || session.ExpiresAt <= clock.UtcNow)
        {
            await cache.RemoveAsync(key, cancellationToken);
            return null;
        }

        return session;
    }

    public Task DeleteAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        return IsValidToken(token)
            ? cache.RemoveAsync(GetCacheKey(token), cancellationToken)
            : Task.CompletedTask;
    }

    private static bool IsValidToken(string token)
    {
        if (token.Length != 64)
        {
            return false;
        }

        foreach (var character in token)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetCacheKey(string token)
    {
        var tokenHash = SHA256.HashData(Encoding.ASCII.GetBytes(token));
        return $"{CacheKeyPrefix}{Convert.ToHexStringLower(tokenHash)}";
    }
}
