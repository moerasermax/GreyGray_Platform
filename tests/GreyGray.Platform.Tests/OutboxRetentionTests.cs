using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.Platform.Tests;

/// <summary>
/// BE-54 / #57：<c>platform.outbox_message</c> 的保存期限。
/// </summary>
/// <remarks>
/// 這不是「順手加的清理功能」。ADR-039 把收件人真實姓名與手機放進 <c>CheckoutCompleted</c>
/// 的 payload，而這張表原本<b>永遠不刪</b>——沒有保存期限，個資就永久留存，
/// 「快照存明文」的取捨不成立。所以四條邊界都要有測試：
/// 已投遞且過期<b>刪</b>、已投遞未過期<b>不刪</b>、死信<b>不刪</b>、未投遞<b>不刪</b>。
/// </remarks>
public sealed class OutboxRetentionTests : IAsyncLifetime
{
    private const string DatabaseSchema = """
        DROP SCHEMA IF EXISTS platform CASCADE;

        CREATE SCHEMA platform;

        CREATE TABLE platform.outbox_message (
            id              uuid        PRIMARY KEY,
            tenant_id       uuid        NOT NULL,
            aggregate_type  text        NOT NULL,
            aggregate_id    text        NOT NULL,
            event_type      text        NOT NULL,
            payload         jsonb       NOT NULL,
            occurred_at     timestamptz NOT NULL,
            correlation_id  text        NOT NULL,
            causation_id    text,
            processed_at    timestamptz,
            attempts        int         NOT NULL DEFAULT 0,
            next_attempt_at timestamptz NOT NULL DEFAULT now(),
            last_error      text,
            dead_lettered   boolean     NOT NULL DEFAULT false,
            CONSTRAINT outbox_attempts_non_negative CHECK (attempts >= 0)
        );
        """;

    private static readonly DateTimeOffset Now = new(2026, 9, 19, 3, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();
    private readonly FakeClock _clock = new(Now);

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "#57：只刪『已投遞且過期』——未過期、死信、未投遞都留著")]
    public async Task Only_processed_and_expired_messages_are_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);

        // 保存期限 30 天，所以 cutoff 是 2026-08-20 03:00。
        var expired = await InsertAsync("expired", processedAt: Now.AddDays(-31), deadLettered: false, cancellationToken);
        var fresh = await InsertAsync("fresh", processedAt: Now.AddDays(-29), deadLettered: false, cancellationToken);
        // 死信刻意也給一個過期的 processed_at：只靠「processed_at 非 null」判斷的話會把它掃掉，
        // 而那是還沒處理完的問題，刪了就查不出來了。
        var deadLettered = await InsertAsync("dead", processedAt: Now.AddDays(-99), deadLettered: true, cancellationToken);
        var pending = await InsertAsync("pending", processedAt: null, deadLettered: false, cancellationToken);

        await using var dbContext = new PlatformDbContext(PlatformOptions());
        var sweeper = CreateSweeper(dbContext, 30);

        (await sweeper.PurgeExpiredAsync(100, cancellationToken)).ShouldBe(1);

        var remaining = await RemainingIdsAsync(cancellationToken);
        remaining.ShouldNotContain(expired);
        remaining.ShouldContain(fresh, "還在保存期限內的不能刪。");
        remaining.ShouldContain(deadLettered, "死信一律不刪——那是還沒處理完的問題。");
        remaining.ShouldContain(pending, "還沒投遞成功的刪掉就是丟事件。");

        // 再掃一次：已經沒有可刪的，回 0（而不是又動到別的列）。
        (await sweeper.PurgeExpiredAsync(100, cancellationToken)).ShouldBe(0);
        (await RemainingIdsAsync(cancellationToken)).Count.ShouldBe(3);
    }

    [Fact(DisplayName = "#57：一次最多刪 batchSize 筆，剩下的下一批再刪")]
    public async Task Deletion_is_limited_to_the_batch_size()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);

        for (var index = 0; index < 5; index++)
        {
            await InsertAsync($"old-{index}", Now.AddDays(-40 - index), false, cancellationToken);
        }

        await using var dbContext = new PlatformDbContext(PlatformOptions());
        var sweeper = CreateSweeper(dbContext, 30);

        (await sweeper.PurgeExpiredAsync(2, cancellationToken)).ShouldBe(2);
        (await RemainingIdsAsync(cancellationToken)).Count.ShouldBe(3);
        (await sweeper.PurgeExpiredAsync(2, cancellationToken)).ShouldBe(2);
        (await sweeper.PurgeExpiredAsync(2, cancellationToken)).ShouldBe(1);
        (await RemainingIdsAsync(cancellationToken)).ShouldBeEmpty();
    }

    [Fact(DisplayName = "#57：保存天數改組態就會跟著變，不是寫死的")]
    public async Task Retention_days_come_from_configuration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ResetDatabaseAsync(cancellationToken);
        var tenDaysOld = await InsertAsync("ten-days", Now.AddDays(-10), false, cancellationToken);

        await using var longRetention = new PlatformDbContext(PlatformOptions());
        (await CreateSweeper(longRetention, 30).PurgeExpiredAsync(100, cancellationToken)).ShouldBe(0);
        (await RemainingIdsAsync(cancellationToken)).ShouldContain(tenDaysOld);

        await using var shortRetention = new PlatformDbContext(PlatformOptions());
        (await CreateSweeper(shortRetention, 7).PurgeExpiredAsync(100, cancellationToken)).ShouldBe(1);
        (await RemainingIdsAsync(cancellationToken)).ShouldBeEmpty();
    }

    [Fact(DisplayName = "#57：保存天數設成 0 或負數是組態錯誤，要當場丟，不是安靜地永不清理")]
    public void Non_positive_retention_days_are_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => OutboxRetentionPolicy.FromDays(0));
        Should.Throw<ArgumentOutOfRangeException>(() => OutboxRetentionPolicy.FromDays(-1));
        OutboxRetentionPolicy.FromDays(OutboxRetentionPolicy.DefaultDays).Retention
            .ShouldBe(TimeSpan.FromDays(OutboxRetentionPolicy.DefaultDays));
    }

    private OutboxRetentionSweeper CreateSweeper(PlatformDbContext dbContext, int retentionDays) =>
        new(
            dbContext,
            OutboxRetentionPolicy.FromDays(retentionDays),
            _clock,
            NullLogger<OutboxRetentionSweeper>.Instance);

    private async Task<Guid> InsertAsync(
        string aggregateId,
        DateTimeOffset? processedAt,
        bool deadLettered,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO platform.outbox_message (
                id, tenant_id, aggregate_type, aggregate_id, event_type, payload,
                occurred_at, correlation_id, processed_at, next_attempt_at, dead_lettered)
            VALUES (
                @id, @tenant, 'Cart', @aggregate, 'checkout.CheckoutCompleted.v1', '{}'::jsonb,
                @occurred, 'corr', @processed, @occurred, @dead);
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant", TenantId.Default.Value);
        command.Parameters.AddWithValue("aggregate", aggregateId);
        command.Parameters.AddWithValue("occurred", Now.AddDays(-120));
        command.Parameters.AddWithValue(
            "processed",
            processedAt is null ? DBNull.Value : processedAt.Value);
        command.Parameters.AddWithValue("dead", deadLettered);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    private async Task<IReadOnlyList<Guid>> RemainingIdsAsync(CancellationToken cancellationToken)
    {
        await using var verification = new PlatformDbContext(PlatformOptions());
        return await verification.OutboxMessages
            .AsNoTracking()
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(DatabaseSchema, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbContextOptions<PlatformDbContext> PlatformOptions() =>
        new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
}
