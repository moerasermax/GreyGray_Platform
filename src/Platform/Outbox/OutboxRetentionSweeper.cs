using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GreyGray.Platform.Outbox;

/// <summary>
/// <c>platform.outbox_message</c> 的保存期限（#57）。
/// </summary>
/// <remarks>
/// <para>
/// 天數做成組態（<c>Platform:Outbox:RetentionDays</c>），比照 ADR-025 鑑賞期天數的做法，
/// <b>不寫死</b>——正式機與 dev 的合理值不一樣，而改天數不該需要重新編譯。
/// </para>
/// <para>
/// 為什麼這是 ADR-039 的<b>前提條件</b>而不是順手加的功能：收件人真實姓名與手機會隨
/// <c>CheckoutCompleted</c> 進到 outbox 的 payload，而投遞成功後這張表原本
/// <b>永遠不會刪</b>（<see cref="OutboxDispatcher"/> 只寫 <c>ProcessedAt</c>）。
/// 沒有保存期限，個資就永久留存，ADR-039「快照存明文」的取捨不成立。
/// </para>
/// </remarks>
public sealed class OutboxRetentionSweeper(
    PlatformDbContext dbContext,
    OutboxRetentionPolicy policy,
    IClock clock,
    ILogger<OutboxRetentionSweeper> logger)
{
    /// <summary>
    /// 刪除「已投遞且超過保存期限」的訊息，一次最多 <paramref name="batchSize"/> 筆。
    /// </summary>
    /// <remarks>
    /// 三種訊息<b>一律不刪</b>：
    /// <list type="number">
    ///   <item><c>processed_at IS NULL</c>——還沒投遞成功，刪掉就是丟事件。</item>
    ///   <item><c>dead_lettered = true</c>——那是<b>還沒處理完的問題</b>，刪掉就查不出來了。
    ///         它的 <c>processed_at</c> 本來就是 null，這裡仍明寫一次條件，
    ///         免得日後有人讓死信也帶上 <c>processed_at</c> 時無聲地把證據掃掉。</item>
    ///   <item>還沒到期的。</item>
    /// </list>
    /// <c>FOR UPDATE SKIP LOCKED</c>：清理不可以跟正在派送的批次互相卡住。
    /// </remarks>
    /// <returns>這一批實際刪掉的筆數。</returns>
    public async Task<int> PurgeExpiredAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var cutoff = clock.UtcNow - policy.Retention;
        var deleted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM platform.outbox_message
            WHERE id IN (
                SELECT id
                FROM platform.outbox_message
                WHERE processed_at IS NOT NULL
                  AND processed_at < {cutoff}
                  AND dead_lettered = false
                ORDER BY processed_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
            )
            """,
            cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation(
                "Outbox 保存期限清理：刪除 {Deleted} 則 {Cutoff} 之前已投遞的訊息（保存 {Days} 天）。",
                deleted,
                cutoff,
                policy.Retention.TotalDays);
        }

        return deleted;
    }
}

/// <summary>outbox 已投遞訊息的保存期限。</summary>
public sealed record OutboxRetentionPolicy(TimeSpan Retention)
{
    /// <summary>沒有設定 <c>Platform:Outbox:RetentionDays</c> 時的預設值。</summary>
    public const int DefaultDays = 30;

    /// <summary>從組態的天數建立；0 或負數是組態錯誤，要讓行程開機失敗而不是安靜地永不清理。</summary>
    public static OutboxRetentionPolicy FromDays(int days)
    {
        if (days <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(days),
                days,
                "Platform:Outbox:RetentionDays 必須是正整數。");
        }

        return new OutboxRetentionPolicy(TimeSpan.FromDays(days));
    }
}
