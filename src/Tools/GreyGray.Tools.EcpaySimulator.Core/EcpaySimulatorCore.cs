using System.Globalization;
using System.Net;
using System.Text;
using GreyGray.Modules.Payment.Infra;

namespace GreyGray.Tools.EcpaySimulator.Core;

/// <summary>模擬器上「使用者按了哪一顆按鈕」。</summary>
public enum SimulatedOutcome
{
    /// <summary>模擬付款成功。</summary>
    Success = 1,

    /// <summary>模擬付款失敗。</summary>
    Failure = 2,
}

/// <summary>結帳表單的檢查結果。<see cref="IsValid"/> 為 false 時，兩個欄位就是要顯示給人看的錯誤。</summary>
public sealed record CheckoutValidation(bool IsValid, string RtnCode, string RtnMsg)
{
    /// <summary>通過。</summary>
    public static CheckoutValidation Ok { get; } = new(true, "1", "OK");
}

/// <summary>電子地圖表單的檢查結果。</summary>
public sealed record CvsMapValidation(bool IsValid, string ErrorMessage)
{
    /// <summary>通過。</summary>
    public static CvsMapValidation Ok { get; } = new(true, string.Empty);
}

/// <summary>dev 假地圖上的一間門市。</summary>
public sealed record CvsMapStore(
    string Code,
    string Name,
    string Address,
    string Telephone,
    bool IsOutlying);

/// <summary>
/// 綠界模擬器的純函式層（ADR-029）：驗結帳表單、組付款結果通知、組退刷回應。
/// <b>端點只是薄殼</b>——邏輯全部在這裡，測試才碰得到。
/// </summary>
/// <remarks>
/// 這支扮演的是<b>綠界的伺服器</b>，不是我們的 adapter。所以它必須用跟正式碼完全同一套
/// 簽章演算法（<c>EcpayGateway.ComputeCheckMacValue</c>，走 <c>InternalsVisibleTo</c> 重用），
/// 否則「模擬器過了」就不代表「真綠界會過」。
/// </remarks>
public static class EcpaySimulatorCore
{
    /// <summary>模擬器只配假憑證，假的一律用這個前綴——在後台與 DB 一眼看得出來。</summary>
    public const string FakePrefix = "DEVFAKE";

    /// <summary>綠界的簽章錯誤代碼。</summary>
    public const string CheckMacValueErrorCode = "10200073";

    /// <summary>綠界的簽章錯誤訊息。</summary>
    public const string CheckMacValueErrorMessage = "CheckMacValue Error";

    /// <summary>模擬付款失敗時回的代碼（非 1，才會走到 <c>PaymentFailed</c> 那一條）。</summary>
    public const string FailureRtnCode = "10100251";

    private static readonly string[] RequiredCheckoutFields =
    [
        "MerchantID", "MerchantTradeNo", "MerchantTradeDate", "TotalAmount", "ReturnURL",
    ];

    /// <summary>固定的假門市；名稱明確標示 dev，避免被誤認成正式門市。</summary>
    public static IReadOnlyList<CvsMapStore> FakeStores { get; } =
    [
        new("991001", "台北模擬門市（dev）", "台北市中正區模擬路 1 號", "02-20000001", false),
        new("991002", "台中模擬門市（dev）", "台中市西區模擬路 2 號", "04-20000002", false),
        new("991003", "澎湖模擬門市（dev）", "澎湖縣馬公市模擬路 3 號", "06-90000003", true),
    ];

    /// <summary>
    /// 台北時區。<b>不要直接寫 <c>FindSystemTimeZoneById("Asia/Taipei")</c></b>——
    /// <c>InvariantGlobalization</c> 關掉 ICU 之後，Windows 上查不到 IANA 那個名字
    /// （理由與證據見 <c>GreyGray.Modules.Payment.Core.TaipeiTime</c>）。
    /// 這裡自己留一份，是因為那個型別是 Payment.Core 的 internal，工具不該伸手進去；
    /// 正確的長期做法是把它搬到 Shared.Kernel 讓全專案共用一份。
    /// </summary>
    public static TimeZoneInfo TaipeiTimeZone { get; } = ResolveTaipeiTimeZone();

    /// <summary>把 UTC 時刻換成台北時刻。</summary>
    public static DateTimeOffset ToTaipei(DateTimeOffset utcNow)
        => TimeZoneInfo.ConvertTime(utcNow, TaipeiTimeZone);

    /// <summary>用正式碼那一支演算法簽章。</summary>
    public static string Sign(IReadOnlyDictionary<string, string> fields, string hashKey, string hashIv)
        => EcpayGateway.ComputeCheckMacValue(fields, hashKey, hashIv);

    /// <summary>驗簽。大小寫不敏感（綠界送的是大寫十六進位）。</summary>
    public static bool VerifySignature(
        IReadOnlyDictionary<string, string> fields,
        string hashKey,
        string hashIv)
        => fields.TryGetValue("CheckMacValue", out var supplied) &&
           supplied.Length == 64 &&
           string.Equals(supplied, Sign(fields, hashKey, hashIv), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 檢查結帳表單。先驗簽（跟真綠界一樣，簽錯就什麼都不做），再看必要欄位與 MerchantID。
    /// </summary>
    public static CheckoutValidation ValidateCheckoutForm(
        IReadOnlyDictionary<string, string> fields,
        string merchantId,
        string hashKey,
        string hashIv)
    {
        if (!VerifySignature(fields, hashKey, hashIv))
        {
            return new CheckoutValidation(false, CheckMacValueErrorCode, CheckMacValueErrorMessage);
        }

        foreach (var key in RequiredCheckoutFields)
        {
            if (!fields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return new CheckoutValidation(false, "10200052", $"缺少必要欄位 {key}。");
            }
        }

        if (!StringComparer.Ordinal.Equals(fields["MerchantID"], merchantId))
        {
            return new CheckoutValidation(
                false,
                "10200002",
                $"MerchantID '{fields["MerchantID"]}' 與模擬器設定的特店代號不符。");
        }

        if (!long.TryParse(
                fields["TotalAmount"],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var total) || total <= 0)
        {
            return new CheckoutValidation(false, "10200029", "TotalAmount 必須是大於零的整數。");
        }

        return CheckoutValidation.Ok;
    }

    /// <summary>驗證送往 7-ELEVEN 電子地圖的表單。</summary>
    public static CvsMapValidation ValidateCvsMapForm(IReadOnlyDictionary<string, string> fields)
    {
        var merchantId = fields.GetValueOrDefault("MerchantID", string.Empty);
        if (!merchantId.StartsWith(FakePrefix, StringComparison.Ordinal))
        {
            return new(false, $"MerchantID 必須以 {FakePrefix} 開頭。");
        }

        if (!StringComparer.Ordinal.Equals(
                fields.GetValueOrDefault("LogisticsType", string.Empty),
                "CVS"))
        {
            return new(false, "LogisticsType 必須是 CVS。");
        }

        if (!StringComparer.Ordinal.Equals(
                fields.GetValueOrDefault("LogisticsSubType", string.Empty),
                "UNIMARTC2C"))
        {
            return new(false, "LogisticsSubType 必須是 UNIMARTC2C。");
        }

        var replyUrlText = fields.GetValueOrDefault("ServerReplyURL", string.Empty);
        if (!Uri.TryCreate(replyUrlText, UriKind.Absolute, out var replyUrl)
            || (replyUrl.Scheme != Uri.UriSchemeHttp && replyUrl.Scheme != Uri.UriSchemeHttps))
        {
            return new(false, "ServerReplyURL 必須是絕對的 http 或 https 網址。");
        }

        var extraData = fields.GetValueOrDefault("ExtraData", string.Empty);
        if (string.IsNullOrEmpty(extraData) || extraData.Length > 20)
        {
            return new(false, "ExtraData 必須有值且不得超過 20 個字元。");
        }

        return CvsMapValidation.Ok;
    }

    /// <summary>依地圖請求與假門市組出綠界同形的九個回傳欄位。</summary>
    public static IReadOnlyDictionary<string, string> BuildCvsMapReplyFields(
        IReadOnlyDictionary<string, string> mapForm,
        CvsMapStore store,
        string? merchantIdOverride = null) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MerchantID"] = merchantIdOverride
                ?? mapForm.GetValueOrDefault("MerchantID", string.Empty),
            ["MerchantTradeNo"] = mapForm.GetValueOrDefault("MerchantTradeNo", string.Empty),
            ["LogisticsSubType"] = mapForm.GetValueOrDefault("LogisticsSubType", string.Empty),
            ["CVSStoreID"] = store.Code,
            ["CVSStoreName"] = store.Name,
            ["CVSAddress"] = store.Address,
            ["CVSTelephone"] = store.Telephone,
            ["CVSOutSide"] = store.IsOutlying ? "1" : "0",
            ["ExtraData"] = mapForm.GetValueOrDefault("ExtraData", string.Empty),
        };

    /// <summary>
    /// 畫出可操作的 dev 假地圖。所有來自表單與門市清單的值都在這個純函式裡 HtmlEncode，
    /// Web 端點只負責接線，避免漏包一個值就形成 HTML 注入。
    /// </summary>
    public static string RenderCvsMapPage(
        IReadOnlyDictionary<string, string> mapForm,
        IReadOnlyList<CvsMapStore>? stores = null)
    {
        stores ??= FakeStores;
        var replyUrl = mapForm.GetValueOrDefault("ServerReplyURL", string.Empty);
        var cards = new StringBuilder();
        foreach (var store in stores)
        {
            cards.Append("<form method=\"post\" action=\"")
                .Append(E(replyUrl))
                .Append("\" class=\"card\">")
                .Append(RenderHiddenFields(BuildCvsMapReplyFields(mapForm, store)))
                .Append("<h2>").Append(E(store.Name)).Append("</h2>")
                .Append("<p>").Append(E(store.Code)).Append("｜")
                .Append(E(store.Address)).Append("</p>")
                .Append("<button type=\"submit\">選擇這家門市</button></form>");
        }

        if (stores.Count > 0)
        {
            cards.Append("<form method=\"post\" action=\"")
                .Append(E(replyUrl))
                .Append("\" class=\"card forged\">")
                .Append(RenderHiddenFields(BuildCvsMapReplyFields(
                    mapForm,
                    stores[0],
                    "FORGED-MERCHANT")))
                .Append("<button type=\"submit\">偽造回傳：MerchantID 錯</button></form>");
        }

        return $$"""
            <!DOCTYPE html>
            <html lang="zh-Hant">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>7-ELEVEN 假地圖｜綠界模擬器（dev）</title>
            <style>
            body{font-family:system-ui,"Noto Sans TC",sans-serif;margin:0;background:#f4f4f5;color:#18181b}
            main{max-width:40rem;margin:0 auto;padding:1.5rem}.banner{background:#7f1d1d;color:#fff;padding:.75rem 1.5rem;font-weight:700}
            .card{background:#fff;border-radius:.75rem;padding:1.25rem;margin-bottom:1rem;box-shadow:0 1px 3px rgba(0,0,0,.1)}
            button{font-size:1rem;padding:.7rem 1.2rem;border-radius:.5rem;border:0;cursor:pointer;width:100%;background:#15803d;color:#fff}
            .forged button{background:#b91c1c}</style>
            </head>
            <body><div class="banner">綠界 7-ELEVEN 電子地圖模擬器（dev）</div>
            <main><h1>請選擇假門市</h1>{{cards}}</main></body>
            </html>
            """;
    }

    /// <summary>
    /// 產生模擬的綠界交易編號：<c>DEVFAKE</c> ＋ 13 位數字，共 20 字。
    /// 前綴是刻意的——它會進 <c>ProviderTransactionId</c>，之後在後台與 DB 一眼看得出是模擬的。
    /// </summary>
    public static string CreateTradeNo(DateTimeOffset taipeiNow, int randomDigit)
    {
        var digits = taipeiNow.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture);
        return $"{FakePrefix}{digits}{Math.Abs(randomDigit) % 10}";
    }

    /// <summary>
    /// 組出綠界 AIO「付款結果通知」的欄位（含 <c>CheckMacValue</c>）。
    /// <b>刻意不送 <c>SimulatePaid=1</c></b>：那是綠界測試站的旗標，會走到
    /// <c>AllowSimulatedPaid</c> 那條特例；模擬器要通過的是<b>正式</b>那條判斷。
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildPaymentNotification(
        IReadOnlyDictionary<string, string> checkoutForm,
        SimulatedOutcome outcome,
        string tradeNo,
        DateTimeOffset paidAtTaipei,
        string hashKey,
        string hashIv)
    {
        var notification = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MerchantID"] = checkoutForm.GetValueOrDefault("MerchantID", string.Empty),
            ["MerchantTradeNo"] = checkoutForm.GetValueOrDefault("MerchantTradeNo", string.Empty),
            ["StoreID"] = string.Empty,
            ["RtnCode"] = outcome == SimulatedOutcome.Success ? "1" : FailureRtnCode,
            ["RtnMsg"] = outcome == SimulatedOutcome.Success ? "交易成功" : "模擬付款失敗",
            ["TradeNo"] = tradeNo,
            ["TradeAmt"] = checkoutForm.GetValueOrDefault("TotalAmount", string.Empty),
            ["PaymentDate"] = paidAtTaipei.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["PaymentType"] = "Credit_CreditCard",
            ["PaymentTypeChargeFee"] = "0",
            ["TradeDate"] = checkoutForm.GetValueOrDefault("MerchantTradeDate", string.Empty),
            ["SimulatePaid"] = "0",
            ["CustomField1"] = string.Empty,
            ["CustomField2"] = string.Empty,
            ["CustomField3"] = string.Empty,
            ["CustomField4"] = string.Empty,
        };
        notification["CheckMacValue"] = Sign(notification, hashKey, hashIv);
        return notification;
    }

    /// <summary>
    /// 組出退刷（<c>/CreditDetail/DoAction</c>）的回應本體。
    /// 形狀是 form-urlencoded，因為 <c>EcpayGateway.ParseFormEncodedResponse</c> 就是這樣讀的。
    /// </summary>
    public static string BuildDoActionResponse(
        IReadOnlyDictionary<string, string> fields,
        string hashKey,
        string hashIv)
    {
        var merchantId = fields.GetValueOrDefault("MerchantID", string.Empty);
        var merchantTradeNo = fields.GetValueOrDefault("MerchantTradeNo", string.Empty);
        var tradeNo = fields.GetValueOrDefault("TradeNo", string.Empty);

        if (!VerifySignature(fields, hashKey, hashIv))
        {
            return FormEncode(
                merchantId, merchantTradeNo, tradeNo, CheckMacValueErrorCode, CheckMacValueErrorMessage);
        }

        return StringComparer.Ordinal.Equals(fields.GetValueOrDefault("Action", string.Empty), "R")
            ? FormEncode(merchantId, merchantTradeNo, tradeNo, "1", "OK")
            : FormEncode(merchantId, merchantTradeNo, tradeNo, "0", "模擬器只支援 Action=R（退刷）。");
    }

    private static TimeZoneInfo ResolveTaipeiTimeZone()
    {
        foreach (var id in new[] { "Asia/Taipei", "Taipei Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // 換下一個候選名字。
            }
        }

        throw new InvalidOperationException("這台機器查不到台北時區（Asia/Taipei／Taipei Standard Time）。");
    }

    private static string FormEncode(
        string merchantId,
        string merchantTradeNo,
        string tradeNo,
        string rtnCode,
        string rtnMsg)
        => $"MerchantID={Uri.EscapeDataString(merchantId)}" +
           $"&MerchantTradeNo={Uri.EscapeDataString(merchantTradeNo)}" +
           $"&TradeNo={Uri.EscapeDataString(tradeNo)}" +
           $"&RtnCode={Uri.EscapeDataString(rtnCode)}" +
           $"&RtnMsg={Uri.EscapeDataString(rtnMsg)}";

    private static string RenderHiddenFields(IReadOnlyDictionary<string, string> fields)
    {
        var html = new StringBuilder();
        foreach (var pair in fields)
        {
            html.Append("<input type=\"hidden\" name=\"")
                .Append(E(pair.Key))
                .Append("\" value=\"")
                .Append(E(pair.Value))
                .Append("\">");
        }

        return html.ToString();
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
