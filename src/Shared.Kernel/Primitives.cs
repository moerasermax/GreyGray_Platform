namespace Daigou.Shared.Kernel;

/// <summary>
/// 租戶識別。<b>M0 只留欄位，不做隔離</b>（見 docs/00-decisions.md ADR-006）。
/// 現階段全系統一律使用 <see cref="Default"/>。
/// </summary>
public readonly record struct TenantId(Guid Value)
{
    /// <summary>單一租戶時期的固定值。不要改這個常數，改了等於棄掉既有資料。</summary>
    public static readonly TenantId Default = new(new Guid("00000000-0000-0000-0000-000000000001"));

    public override string ToString() => Value.ToString("N");
}

/// <summary>
/// 時間一律經過這裡取得，不要直接呼叫 <c>DateTimeOffset.UtcNow</c>——
/// 否則截團、逾期未付、詢價逾時放行這三條時間驅動的規則測不起來。
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>台北時區的今天。開團／截團／出發日一律用這個，不要用 UTC 的日期。</summary>
    DateOnly TodayInTaipei { get; }
}

/// <summary>貫穿 API → 模組 → outbox → worker → 外部整合的關聯識別。</summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }

    string? CausationId { get; }

    TenantId TenantId { get; }
}

/// <summary>
/// 包裹尺寸，公分。M1a 不拿來算運費（一口價），但商品建檔時就要填——
/// 等 M3 啟用材積重計費時資料已經在那裡了，回頭補幾百筆是純粹的浪費。
/// </summary>
public readonly record struct Dimensions(int LengthCm, int WidthCm, int HeightCm)
{
    /// <summary>材積重（公克）＝ 長 × 寬 × 高 ÷ divisor × 1000。黑貓慣例 divisor = 6000。</summary>
    public int VolumetricWeightGram(int divisor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisor);
        return (int)((long)LengthCm * WidthCm * HeightCm * 1000 / divisor);
    }
}
