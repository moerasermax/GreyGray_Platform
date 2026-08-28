namespace Daigou.Shared.Kernel;

/// <summary>
/// 幣別。最小單位的小數位數各幣別不同——JPY 與 KRW 沒有小數位，
/// 這是 Money 一律用整數最小單位儲存時最容易踩到的坑。
/// </summary>
public enum Currency
{
    TWD = 901,
    JPY = 392,
    USD = 840,
    KRW = 410,
    EUR = 978,
    HKD = 344,
    CNY = 156,
    THB = 764,
    GBP = 826,
    SGD = 702,
}

public static class CurrencyExtensions
{
    /// <summary>該幣別最小單位的小數位數（ISO 4217 exponent）。</summary>
    public static int MinorUnitDigits(this Currency currency) => currency switch
    {
        Currency.JPY => 0,
        Currency.KRW => 0,
        _ => 2,
    };

    /// <summary>1 元等於幾個最小單位。TWD = 100（分），JPY = 1（円）。</summary>
    public static long MinorUnitsPerUnit(this Currency currency) => currency switch
    {
        Currency.JPY => 1L,
        Currency.KRW => 1L,
        _ => 100L,
    };
}
