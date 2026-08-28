using System.Data;
using System.Data.Common;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GreyGray.Platform.Idempotency;

/// <summary>
/// PostgreSQL 冪等儲存。成功取得處理權後會在記憶體保留 completion fencing token，
/// 因此此實例必須與 <see cref="DbContext"/> 使用相同的 scoped lifetime。
/// </summary>
public sealed class IdempotencyStore : IIdempotencyStore
{
    private const string InFlightStatus = "IN_FLIGHT";
    private const string CompletedStatus = "COMPLETED";
    private const string AbandonedStatus = "ABANDONED";
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(24);

    private readonly PlatformDbContext _dbContext;
    private readonly IClock _clock;
    private readonly TimeSpan _retention;
    private readonly Dictionary<(string Key, string Scope), DateTimeOffset> _leases = new();

    public IdempotencyStore(PlatformDbContext dbContext, IClock clock)
        : this(dbContext, clock, DefaultRetention)
    {
    }

    public IdempotencyStore(
        PlatformDbContext dbContext,
        IClock clock,
        TimeSpan retention)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(clock);
        if (retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }

        _dbContext = dbContext;
        _clock = clock;
        _retention = retention;
    }

    public async Task<(IdempotencyOutcome Outcome, string? cachedResponse)> TryBeginAsync(
        string key,
        string scope,
        string requestHash,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(key, scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);

        var now = _clock.UtcNow;
        var connection = _dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using (var command = CreateCommand(connection, """
                INSERT INTO platform.idempotency_key AS current_entry
                    (key, scope, request_hash, status, response_snapshot, created_at, expires_at)
                VALUES
                    (@key, @scope, @request_hash, 'IN_FLIGHT', NULL, @now, @expires_at)
                ON CONFLICT (key, scope) DO UPDATE SET
                    status = 'IN_FLIGHT',
                    response_snapshot = NULL,
                    created_at = GREATEST(
                        EXCLUDED.created_at,
                        current_entry.created_at + INTERVAL '1 microsecond'),
                    expires_at = GREATEST(
                        EXCLUDED.created_at,
                        current_entry.created_at + INTERVAL '1 microsecond') + @retention
                WHERE current_entry.request_hash = EXCLUDED.request_hash
                  AND (current_entry.status = 'ABANDONED'
                       OR current_entry.expires_at <= EXCLUDED.created_at)
                RETURNING created_at;
                """))
            {
                AddParameter(command, "key", key);
                AddParameter(command, "scope", scope);
                AddParameter(command, "request_hash", requestHash);
                AddParameter(command, "now", now);
                AddParameter(command, "expires_at", now.Add(_retention));
                AddParameter(command, "retention", _retention);

                var token = await command.ExecuteScalarAsync(cancellationToken);
                if (token is not null and not DBNull)
                {
                    var lease = token switch
                    {
                        DateTimeOffset value => value,
                        DateTime value => new DateTimeOffset(
                            value.Kind == DateTimeKind.Utc
                                ? value
                                : DateTime.SpecifyKind(value, DateTimeKind.Utc)),
                        _ => throw new InvalidOperationException(
                            $"PostgreSQL timestamptz 回傳了未預期的 CLR 型別 '{token.GetType().FullName}'。"),
                    };
                    _leases[(key, scope)] = lease;
                    return (IdempotencyOutcome.Proceed, null);
                }
            }

            // ON CONFLICT 可能等待原 statement snapshot 尚不可見的資料列，
            // 因此用下一個 statement 明確讀取已提交的勝出者。
            await using var lookup = CreateCommand(connection, """
                SELECT request_hash,
                       status,
                       response_snapshot #>> '{}'
                FROM platform.idempotency_key
                WHERE key = @key AND scope = @scope;
                """);
            AddParameter(lookup, "key", key);
            AddParameter(lookup, "scope", scope);

            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "解析並行冪等請求時，對應的資料列已消失。");
            }

            var storedHash = reader.GetString(0);
            if (!string.Equals(storedHash, requestHash, StringComparison.Ordinal))
            {
                return (IdempotencyOutcome.KeyReusedWithDifferentPayload, null);
            }

            var status = reader.GetString(1);
            return status switch
            {
                CompletedStatus when !reader.IsDBNull(2) => (
                    IdempotencyOutcome.AlreadyCompleted,
                    reader.GetString(2)),
                CompletedStatus => throw new InvalidOperationException(
                    "已完成的冪等資料列必須包含回應快照。"),
                InFlightStatus or AbandonedStatus => (IdempotencyOutcome.InFlight, null),
                _ => throw new InvalidOperationException(
                    $"scope '{scope}' 出現未知的冪等狀態 '{status}'。"),
            };
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task CompleteAsync(
        string key,
        string scope,
        string responseSnapshot,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(key, scope);
        ArgumentNullException.ThrowIfNull(responseSnapshot);

        if (!_leases.TryGetValue((key, scope), out var lease))
        {
            throw new InvalidOperationException(
                "目前 scoped 冪等儲存並未持有此 key 的有效 lease。");
        }

        var now = _clock.UtcNow;
        var affected = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE platform.idempotency_key
            SET status = 'COMPLETED',
                response_snapshot = to_jsonb({responseSnapshot}::text),
                expires_at = {now.Add(_retention)}
            WHERE key = {key}
              AND scope = {scope}
              AND status = 'IN_FLIGHT'
              AND created_at = {lease}
              AND expires_at > {now};
            """, cancellationToken);

        _leases.Remove((key, scope));
        if (affected != 1)
        {
            throw new InvalidOperationException(
                "冪等 lease 已過期，或在完成前已被其他請求接手。");
        }
    }

    public async Task AbandonAsync(
        string key,
        string scope,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(key, scope);
        if (!_leases.Remove((key, scope), out var lease))
        {
            return;
        }

        var now = _clock.UtcNow;
        await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE platform.idempotency_key
            SET status = 'ABANDONED',
                expires_at = {now}
            WHERE key = {key}
              AND scope = {scope}
              AND status = 'IN_FLIGHT'
              AND created_at = {lease};
            """, cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection, string commandText)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void ValidateIdentity(string key, string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (key.Length > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(key), "冪等 key 最多只能有 255 個字元。");
        }
    }
}
