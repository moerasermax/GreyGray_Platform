using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GreyGray.Shared.Kernel.Json;

/// <summary>
/// 把所有 <c>readonly record struct XxxId(Guid Value)</c> 序列化成<b>純字串</b>，
/// 例如 <c>"0198c3d4e5f607189abc0123456789ab"</c>。
/// </summary>
/// <remarks>
/// <b>為什麼需要這個：</b>System.Text.Json 對 <c>record struct CustomerId(Guid Value)</c> 的預設行為是
/// 序列化成物件 <c>{"value":"…"}</c>。全專案有四十幾個這種強型別 ID，
/// 如果放著不管，outbox 的 payload 會多一層無意義的包裝，而且 API 回應也會跟著變醜。
/// <para>
/// 更要命的是：outbox 裡的 JSON 一旦有正式資料就<b>改不動了</b>——
/// 改形狀等於讓所有未派送的訊息反序列化失敗。所以這件事必須在寫第一行業務邏輯前定死。
/// </para>
/// <para>
/// 反射只發生在「第一次遇到某個型別」時（建 converter 那一刻），
/// 之後走編譯好的 delegate，不是每則訊息都反射。
/// </para>
/// </remarks>
public sealed class GuidIdJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        // Nullable<Guid>（也就是 Guid?）剛好滿足下面每一個條件：是 value type、非 primitive、
        // 非 enum、Value 屬性的型別是 Guid、也有吃 Guid 的建構式（Nullable(Guid value)）。
        // 但 GuidIdJsonConverter<TId> 要求 TId : struct，而 C# 規格明文把 Nullable<T>
        // 排除在 struct 限制之外，MakeGenericType 會在執行期直接丟 TypeLoadException。
        // 裸 Guid? 與 XxxId? 都要在這裡排除，交給 System.Text.Json 內建的 NullableConverter
        // 處理，它會把底層型別（Guid 或 XxxId）交回這個 factory 再判斷一次。
        if (Nullable.GetUnderlyingType(typeToConvert) is not null)
        {
            return false;
        }

        if (!typeToConvert.IsValueType || typeToConvert.IsPrimitive || typeToConvert.IsEnum)
        {
            return false;
        }

        var valueProperty = typeToConvert.GetProperty(
            "Value",
            BindingFlags.Public | BindingFlags.Instance);

        return valueProperty?.PropertyType == typeof(Guid)
               && typeToConvert.GetConstructor([typeof(Guid)]) is not null;
    }

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(GuidIdJsonConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>單一強型別 ID 的 converter，由 <see cref="GuidIdJsonConverterFactory"/> 建出來。</summary>
internal sealed class GuidIdJsonConverter<TId> : JsonConverter<TId>
    where TId : struct
{
    private static readonly Func<Guid, TId> Wrap = BuildWrap();
    private static readonly Func<TId, Guid> Unwrap = BuildUnwrap();

    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"{typeof(TId).Name} 必須是字串形式的 GUID，實際收到 {reader.TokenType}。");
        }

        var text = reader.GetString();
        if (!Guid.TryParse(text, out var value))
        {
            throw new JsonException($"{typeof(TId).Name} 的值不是合法的 GUID：{text}");
        }

        return Wrap(value);
    }

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
        => writer.WriteStringValue(Unwrap(value).ToString("N"));

    /// <summary>property name 的位置也用同一種字串形式，才不會出現 key 與 value 兩套格式。</summary>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
        => writer.WritePropertyName(Unwrap(value).ToString("N"));

    public override TId ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => Wrap(Guid.Parse(reader.GetString()!));

    private static Func<Guid, TId> BuildWrap()
    {
        var constructor = typeof(TId).GetConstructor([typeof(Guid)])
            ?? throw new InvalidOperationException($"{typeof(TId).Name} 沒有接受單一 Guid 的建構式。");

        var parameter = Expression.Parameter(typeof(Guid), "value");
        return Expression.Lambda<Func<Guid, TId>>(Expression.New(constructor, parameter), parameter).Compile();
    }

    private static Func<TId, Guid> BuildUnwrap()
    {
        var parameter = Expression.Parameter(typeof(TId), "id");
        return Expression.Lambda<Func<TId, Guid>>(Expression.Property(parameter, "Value"), parameter).Compile();
    }
}
