namespace Daigou.Shared.Kernel;

/// <summary>
/// 金額。一律以 <b>整數最小單位</b> 儲存與運算，禁用 float / double / decimal 當金額欄位。
/// TWD 的最小單位是「分」：NT$180 = 18000。
/// </summary>
/// <remarks>
/// 不變式：跨幣別的算術一律丟例外，不做隱式換匯。要換匯必須明確經過
/// <see cref="FxSnapshot"/>，並在 <see cref="MoneyPair"/> 裡留下依據。
/// </remarks>
public readonly record struct Money(long AmountMinor, Currency Currency)
    : IComparable<Money>
{
    public static Money Zero(Currency currency) => new(0, currency);

    public static Money OfMajor(decimal major, Currency currency)
    {
        var factor = currency.MinorUnitsPerUnit();
        var minor = decimal.Round(major * factor, 0, MidpointRounding.ToEven);
        return new Money((long)minor, currency);
    }

    public bool IsZero => AmountMinor == 0;

    public bool IsNegative => AmountMinor < 0;

    public Money Negate() => this with { AmountMinor = -AmountMinor };

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return this with { AmountMinor = checked(AmountMinor + other.AmountMinor) };
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return this with { AmountMinor = checked(AmountMinor - other.AmountMinor) };
    }

    public Money MultiplyByQuantity(int quantity)
        => this with { AmountMinor = checked(AmountMinor * quantity) };

    /// <summary>
    /// 按權重分帳，且保證分完之後合計與原金額<b>完全相等</b>（餘數由前面幾份各多吃 1）。
    /// 用在把一筆運費或折扣攤到多個 OrderLine 上——不可以各自四捨五入，那會湊不回原數。
    /// </summary>
    public IReadOnlyList<Money> AllocateByWeights(IReadOnlyList<long> weights)
    {
        ArgumentOutOfRangeException.ThrowIfZero(weights.Count);

        var totalWeight = 0L;
        foreach (var weight in weights)
        {
            totalWeight += weight;
        }

        if (totalWeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weights), "權重合計必須為正數。");
        }

        var result = new Money[weights.Count];
        var allocated = 0L;
        for (var i = 0; i < weights.Count; i++)
        {
            var share = AmountMinor * weights[i] / totalWeight;
            result[i] = this with { AmountMinor = share };
            allocated += share;
        }

        var remainder = AmountMinor - allocated;
        for (var i = 0; remainder != 0; i = (i + 1) % weights.Count)
        {
            var step = remainder > 0 ? 1 : -1;
            result[i] = result[i] with { AmountMinor = result[i].AmountMinor + step };
            remainder -= step;
        }

        return result;
    }

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(other);
        return AmountMinor.CompareTo(other.AmountMinor);
    }

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public override string ToString() => $"{Currency}:{AmountMinor}";

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new InvalidOperationException(
                $"不可混算幣別：{Currency} 與 {other.Currency}。換匯必須明確經過 FxSnapshot。");
        }
    }
}
