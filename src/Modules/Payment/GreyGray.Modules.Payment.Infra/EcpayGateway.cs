using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using GreyGray.Modules.Payment.Core;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Payment.Infra;

internal sealed class EcpayGateway(
    EcpaySettings settings,
    string hashKey,
    string hashIv,
    HttpClient httpClient) : IEcpayGateway
{
    private const string TransitionalPaymentMethod = "Credit";

    public IReadOnlyDictionary<string, string> CreateCheckoutFields(
        string merchantTradeNo,
        Money amount,
        string description,
        Uri returnUrl,
        Uri clientBackUrl,
        DateTimeOffset createdAt)
    {
        // 為什麼不用 ConvertTimeBySystemTimeZoneId(createdAt, "Asia/Taipei")：見 TaipeiTime 的註解。
        // 那個寫法在 Windows ＋ InvariantGlobalization 下丟 TimeZoneNotFoundException，
        // 也就是說付款發動從來沒有成功過（BE-40 的第一條測試才撞到）。換算結果不變。
        var taipei = TimeZoneInfo.ConvertTime(createdAt, TaipeiTime.Zone);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MerchantID"] = settings.MerchantId,
            ["MerchantTradeNo"] = merchantTradeNo,
            ["MerchantTradeDate"] = taipei.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["PaymentType"] = "aio",
            ["TotalAmount"] = (amount.AmountMinor / Currency.TWD.MinorUnitsPerUnit())
                .ToString(CultureInfo.InvariantCulture),
            ["TradeDesc"] = Normalize(description, 200, "GreyGray order"),
            ["ItemName"] = Normalize(description, 400, "GreyGray order"),
            ["ReturnURL"] = returnUrl.AbsoluteUri,
            // 綠界完成頁的「返回商店」按鈕。少了它，客人付完款就停在綠界頁上沒有路回來（#33）。
            // 它跟其他欄位一樣要進 CheckMacValue，所以放在算簽章之前。
            ["ClientBackURL"] = clientBackUrl.AbsoluteUri,
            // ADR-044 過渡期只收信用卡；BE-66 重新開放時不能只把字串改回 ALL。
            ["ChoosePayment"] = TransitionalPaymentMethod,
            ["EncryptType"] = "1",
        };
        fields["CheckMacValue"] = ComputeCheckMacValue(fields, hashKey, hashIv);
        return fields;
    }

    public bool VerifyCallback(IReadOnlyDictionary<string, string> fields)
    {
        if (!fields.TryGetValue("CheckMacValue", out var supplied) || supplied.Length != 64)
        {
            return false;
        }

        var expected = ComputeCheckMacValue(fields, hashKey, hashIv);
        var suppliedBytes = Encoding.ASCII.GetBytes(supplied.ToUpperInvariant());
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    public async Task<EcpayRefundResult> RequestRefundAsync(
        string merchantTradeNo,
        string providerTransactionId,
        Money amount,
        CancellationToken cancellationToken)
    {
        if (amount.Currency != Currency.TWD || amount.IsNegative || amount.IsZero)
        {
            throw new InvalidOperationException("退款金額必須是大於零的新台幣。");
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MerchantID"] = settings.MerchantId,
            ["MerchantTradeNo"] = merchantTradeNo,
            ["TradeNo"] = providerTransactionId,
            ["Action"] = "R",
            ["TotalAmount"] = (amount.AmountMinor / Currency.TWD.MinorUnitsPerUnit())
                .ToString(CultureInfo.InvariantCulture),
        };
        fields["CheckMacValue"] = ComputeCheckMacValue(fields, hashKey, hashIv);

        using var response = await httpClient.PostAsync(
            settings.CreditDetailUrl,
            new FormUrlEncodedContent(fields),
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // 非 2xx 是技術性失敗（連線、逾時、綠界端錯誤），不是業務失敗——
            // 讓例外往外拋，交給 IdempotentIntegrationEventHandler 回滾＋讓訊息可重送。
            throw new HttpRequestException(
                $"綠界退刷 API 回應非 2xx 狀態碼：{(int)response.StatusCode}。內容：{body}");
        }

        var parsed = ParseFormEncodedResponse(body);
        var rtnCode = parsed.GetValueOrDefault("RtnCode", string.Empty);
        var rtnMsg = parsed.GetValueOrDefault("RtnMsg", string.Empty);
        return new EcpayRefundResult(rtnCode == "1", rtnCode, rtnMsg, body);
    }

    private static IReadOnlyDictionary<string, string> ParseFormEncodedResponse(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            result[key] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }

        return result;
    }

    internal static string ComputeCheckMacValue(
        IReadOnlyDictionary<string, string> fields,
        string hashKey,
        string hashIv)
    {
        var pairs = fields
            .Where(pair => !StringComparer.OrdinalIgnoreCase.Equals(pair.Key, "CheckMacValue"))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}");
        var plaintext = $"HashKey={hashKey}&{string.Join('&', pairs)}&HashIV={hashIv}";
        var encoded = WebUtility.UrlEncode(plaintext)
            .Replace("%2D", "-", StringComparison.OrdinalIgnoreCase)
            .Replace("%5F", "_", StringComparison.OrdinalIgnoreCase)
            .Replace("%2E", ".", StringComparison.OrdinalIgnoreCase)
            .Replace("%21", "!", StringComparison.OrdinalIgnoreCase)
            .Replace("%2A", "*", StringComparison.OrdinalIgnoreCase)
            .Replace("%28", "(", StringComparison.OrdinalIgnoreCase)
            .Replace("%29", ")", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(encoded)));
    }

    private static string Normalize(string value, int maxLength, string fallback)
    {
        var normalized = new string((value ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character) ||
                                char.IsWhiteSpace(character) ||
                                character is '-' or '_' or '#')
            .ToArray())
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = fallback;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
