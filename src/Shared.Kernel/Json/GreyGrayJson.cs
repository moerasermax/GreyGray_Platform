using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace GreyGray.Shared.Kernel.Json;

/// <summary>
/// 全專案唯一的 JSON 形狀定義。<b>outbox payload 與 HTTP API 用同一組設定</b>。
/// </summary>
/// <remarks>
/// <b>為什麼要有這個東西：</b>43 個整合事件會由多個人（或多個 agent）平行寫。
/// 若各自帶各自的 <see cref="JsonSerializerOptions"/>，payload 形狀就會分歧；
/// 而 outbox 裡的 JSON 一旦有正式資料就改不動了——改形狀等於讓未派送的訊息全部反序列化失敗。
/// <para>
/// 所以這裡是<b>唯一的來源</b>。需要調整就改這裡並說明理由，不要在別處另建 options。
/// </para>
/// <para>
/// 效能備註：<see cref="JsonSerializerOptions"/> 第一次使用時會建快取，之後才快。
/// 每次 new 一份等於每次重建快取，那是最常見的 System.Text.Json 效能陷阱——
/// 這也是要共用單一靜態實例的原因。
/// </para>
/// </remarks>
public static class GreyGrayJson
{
    /// <summary>
    /// 標準設定。
    /// <list type="bullet">
    ///   <item>property 名稱 camelCase</item>
    ///   <item>enum 一律寫字串（<c>"ConvenienceStore"</c>，不是 <c>1</c>）——存進 outbox 就是永久紀錄</item>
    ///   <item>GUID 一律寫成無連字號的 32 字元十六進位——<b>強型別 ID 與裸 <c>Guid</c> 都是</b>。
    ///         少了後者，同一個 payload 裡的 <c>eventId</c> 會跟 <c>orderId</c> 格式不同，
    ///         而 <c>eventId</c> 正是消費端去重的 key</item>
    ///   <item><see cref="Money"/> 寫成 <c>{"amountMinor":18000,"currency":"TWD"}</c></item>
    ///   <item><b>null 照寫不省略</b>——payload 要能自我描述，省掉之後看不出是「沒有值」還是「當時沒這個欄位」</item>
    ///   <item>中日韓文字不轉義（<c>UnsafeRelaxedJsonEscaping</c> 之外另外放行 CJK 區段），
    ///         否則備註與姓名在 DB 裡會變成一串 <c>\uXXXX</c>，用 SQL 根本查不動</item>
    /// </list>
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>
    /// 建一份標準設定的複本。只有「必須再加 converter」時才用（例如 EF 的 value converter）。
    /// 一般情況請直接用 <see cref="Options"/>，不要 new。
    /// </summary>
    public static JsonSerializerOptions CreateOptions() => Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            NumberHandling = JsonNumberHandling.Strict,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            Encoder = JavaScriptEncoder.Create(
                UnicodeRanges.BasicLatin,
                UnicodeRanges.CjkUnifiedIdeographs,
                UnicodeRanges.CjkSymbolsandPunctuation,
                UnicodeRanges.HalfwidthandFullwidthForms,
                UnicodeRanges.Hiragana,
                UnicodeRanges.Katakana,
                UnicodeRanges.HangulSyllables),
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new MoneyJsonConverter());
        options.Converters.Add(new GuidJsonConverter());
        options.Converters.Add(new GuidIdJsonConverterFactory());

        return options;
    }
}
