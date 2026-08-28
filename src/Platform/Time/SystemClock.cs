using GreyGray.Shared.Kernel;

namespace GreyGray.Platform.Time;

/// <summary>使用系統時間的正式環境時鐘。</summary>
public sealed class SystemClock : IClock
{
    private static readonly TimeZoneInfo TaipeiTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public DateOnly TodayInTaipei =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, TaipeiTimeZone).DateTime);
}
