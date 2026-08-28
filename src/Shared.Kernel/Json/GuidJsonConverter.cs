using System.Text.Json;
using System.Text.Json.Serialization;

namespace GreyGray.Shared.Kernel.Json;

/// <summary>
/// 裸 <see cref="Guid"/> 也一律寫成無連字號的 32 字元十六進位，
/// 與強型別 ID（<see cref="GuidIdJsonConverterFactory"/>）用同一種格式。
/// </summary>
/// <remarks>
/// <b>為什麼需要這個：</b>沒有它的話，同一個 payload 裡會出現兩種 GUID 格式——
/// <code>
/// "orderId":"0198c3d4111170008000000000000002"       ← 強型別 ID
/// "eventId":"0198c3d4-0000-7000-8000-000000000001"   ← 裸 Guid（STJ 預設）
/// </code>
/// 而 <c>eventId</c> 正是消費端去重的 key（<c>platform.processed_message</c> 的主鍵之一）。
/// 一邊用 <c>Guid.Parse</c> 比、一邊用字串比，就會出現「同一則訊息被處理兩次」——
/// 而那種 bug 只會在正式環境的重送路徑上出現。
/// <para>
/// 讀取端寬鬆：`Guid.TryParse` 兩種格式都吃得下，所以先前若已有帶連字號的資料仍讀得回來。
/// 寫入端只有一種格式。
/// </para>
/// </remarks>
public sealed class GuidJsonConverter : JsonConverter<Guid>
{
    /// <inheritdoc />
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Guid 必須是字串，實際收到 {reader.TokenType}。");
        }

        var text = reader.GetString();
        if (!Guid.TryParse(text, out var value))
        {
            throw new JsonException($"不是合法的 GUID：{text}");
        }

        return value;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("N"));

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
        => writer.WritePropertyName(value.ToString("N"));

    /// <inheritdoc />
    public override Guid ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => Guid.Parse(reader.GetString()!);
}
