using System.Text.Json;
using System.Text.Json.Serialization;

namespace GreyGray.Shared.Kernel.Json;

/// <summary>
/// <see cref="Money"/> 的線上格式，<b>全專案唯一一種</b>：
/// <code>{"amountMinor": 18000, "currency": "TWD"}</code>
/// </summary>
/// <remarks>
/// 三個決定，理由記在這裡免得日後有人「順手改成更好的做法」：
/// <list type="number">
///   <item><b>金額用 JSON number 而非字串</b>：<c>long</c> 最小單位的值域遠在 IEEE-754
///         安全整數範圍內（±2^53），JavaScript 端 <c>JSON.parse</c> 不會失真。
///         真正會失真的是「元」為單位的小數，而那正是本專案不用 decimal 的原因。</item>
///   <item><b>幣別用字串（enum 名稱）</b>：資料庫裡存數字沒關係，但 payload 存進 outbox 之後
///         就是永久紀錄，<c>901</c> 六個月後沒人看得懂，<c>"TWD"</c> 永遠看得懂。</item>
///   <item><b>不提供「元」欄位</b>：多一個衍生欄位就多一個對不起來的機會。
///         顯示是前端的事，換算規則見 <see cref="CurrencyExtensions.MinorUnitsPerUnit"/>。</item>
/// </list>
/// </remarks>
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    private const string AmountPropertyName = "amountMinor";
    private const string CurrencyPropertyName = "currency";

    /// <inheritdoc />
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Money 必須是物件 {{{AmountPropertyName}, {CurrencyPropertyName}}}。");
        }

        long? amountMinor = null;
        Currency? currency = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var propertyName = reader.GetString();
            reader.Read();

            if (string.Equals(propertyName, AmountPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                amountMinor = reader.GetInt64();
            }
            else if (string.Equals(propertyName, CurrencyPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                currency = ParseCurrency(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        if (amountMinor is null || currency is null)
        {
            throw new JsonException($"Money 缺少 {AmountPropertyName} 或 {CurrencyPropertyName}。");
        }

        return new Money(amountMinor.Value, currency.Value);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(AmountPropertyName, value.AmountMinor);
        writer.WriteString(CurrencyPropertyName, value.Currency.ToString());
        writer.WriteEndObject();
    }

    private static Currency ParseCurrency(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (Enum.TryParse<Currency>(text, ignoreCase: false, out var parsed))
            {
                return parsed;
            }

            throw new JsonException($"未知的幣別：{text}");
        }

        // 舊資料若曾以 ISO 4217 數字碼寫入，仍讀得回來。新資料一律寫字串。
        if (reader.TokenType == JsonTokenType.Number && Enum.IsDefined((Currency)reader.GetInt32()))
        {
            return (Currency)reader.GetInt32();
        }

        throw new JsonException($"幣別必須是字串，實際收到 {reader.TokenType}。");
    }
}
