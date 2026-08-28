namespace Daigou.Shared.Kernel;

public readonly record struct FxSnapshotId(Guid Value)
{
    public static FxSnapshotId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

/// <summary>
/// 匯率快照。本專案<b>不產生匯兌損益分錄</b>——現場刷卡或付現即時結清，
/// 存貨成本就是刷卡帳單上的台幣金額。這個型別存在只是為了記錄
/// 「這件在當地賣多少錢」，供下次開團檢討採購價用。
/// </summary>
public sealed record FxSnapshot(
    FxSnapshotId Id,
    Currency From,
    Currency To,
    decimal Rate,
    string Source,
    DateTimeOffset At);

/// <summary>
/// 原幣 ＋ 記帳幣的成對金額。<c>Booking</c> 才是入帳的數字，
/// <c>Original</c> 是資訊欄位，不產生分錄。
/// </summary>
public sealed record MoneyPair(Money Original, Money Booking, FxSnapshotId? Fx);
