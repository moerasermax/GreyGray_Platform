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
    string hashIv) : IEcpayGateway
{
    public IReadOnlyDictionary<string, string> CreateCheckoutFields(
        string merchantTradeNo,
        Money amount,
        string description,
        Uri returnUrl,
        DateTimeOffset createdAt)
    {
        var taipei = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(createdAt, "Asia/Taipei");
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
            ["ChoosePayment"] = "ALL",
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
