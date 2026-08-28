using GreyGray.Modules.Payment.Infra;
using GreyGray.Modules.Payment.Core;
using GreyGray.Shared.Kernel;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.PaymentLedger.Tests;

public sealed class EcpayGatewayTests
{
    [Fact(DisplayName = "CheckMacValue 符合綠界 AIO 官方 SHA-256 範例")]
    public void CheckMacValue_matches_official_aio_vector()
    {
        var fields = new Dictionary<string, string>
        {
            ["TradeDesc"] = "促銷方案",
            ["PaymentType"] = "aio",
            ["MerchantTradeDate"] = "2023/03/12 15:30:23",
            ["MerchantTradeNo"] = "ecpay20230312153023",
            ["MerchantID"] = "3002607",
            ["ReturnURL"] = "https://www.ecpay.com.tw/receive.php",
            ["ItemName"] = "Apple iphone 15",
            ["TotalAmount"] = "30000",
            ["ChoosePayment"] = "ALL",
            ["EncryptType"] = "1",
        };

        EcpayGateway.ComputeCheckMacValue(
                fields,
                "pwFHCqoQZGmho4w6",
                "EkRm7iFT261dpevs")
            .ShouldBe("6C51C9E6888DE861FD62FB1DD17029FC742634498FD813DC43D4243B5685B840");
    }

    [Fact(DisplayName = "CheckMacValue 不把回呼帶來的 CheckMacValue 自己算進去")]
    public void CheckMacValue_excludes_the_supplied_signature()
    {
        var fields = new Dictionary<string, string>
        {
            ["MerchantID"] = "3002607",
            ["MerchantTradeNo"] = "GG123",
            ["CheckMacValue"] = "ATTACKER-CONTROLLED",
        };
        var withoutSignature = fields
            .Where(pair => pair.Key != "CheckMacValue")
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        EcpayGateway.ComputeCheckMacValue(fields, "key", "iv")
            .ShouldBe(EcpayGateway.ComputeCheckMacValue(withoutSignature, "key", "iv"));
    }

    [Fact(DisplayName = "回呼驗簽接受官方向量並拒絕任何欄位竄改")]
    public void Callback_verification_rejects_tampering()
    {
        var fields = new Dictionary<string, string>
        {
            ["MerchantID"] = "3002607",
            ["MerchantTradeNo"] = "ecpay20230312153023",
            ["MerchantTradeDate"] = "2023/03/12 15:30:23",
            ["PaymentType"] = "aio",
            ["TotalAmount"] = "30000",
            ["TradeDesc"] = "促銷方案",
            ["ItemName"] = "Apple iphone 15",
            ["ReturnURL"] = "https://www.ecpay.com.tw/receive.php",
            ["ChoosePayment"] = "ALL",
            ["EncryptType"] = "1",
            ["CheckMacValue"] = "6C51C9E6888DE861FD62FB1DD17029FC742634498FD813DC43D4243B5685B840",
        };
        var gateway = new EcpayGateway(
            new EcpaySettings(
                "3002607",
                new Uri("https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5"),
                TimeSpan.FromMinutes(30),
                TimeSpan.FromMinutes(20),
                false),
            "pwFHCqoQZGmho4w6",
            "EkRm7iFT261dpevs");

        gateway.VerifyCallback(fields).ShouldBeTrue();
        fields["TotalAmount"] = "30001";
        gateway.VerifyCallback(fields).ShouldBeFalse();
    }

    [Fact(DisplayName = "Payment 聚合拒絕錯誤幣別或負手續費")]
    public void Payment_rejects_invalid_provider_fee()
    {
        var payment = Payment.Start(
            GreyGray.Modules.Payment.Contracts.PaymentId.New(),
            TenantId.Default,
            GreyGray.Modules.Ordering.Contracts.OrderId.New(),
            Money.OfMajor(100, Currency.TWD),
            Money.OfMajor(60, Currency.TWD),
            "GG202608280000000001",
            new DateTimeOffset(2026, 8, 28, 4, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 28, 4, 30, 0, TimeSpan.Zero));

        Should.Throw<InvalidOperationException>(() => payment.Capture(
            "1234567890",
            new Money(-1, Currency.TWD),
            new DateTimeOffset(2026, 8, 28, 4, 1, 0, TimeSpan.Zero)));
        Should.Throw<InvalidOperationException>(() => payment.Capture(
            "1234567890",
            Money.OfMajor(1, Currency.USD),
            new DateTimeOffset(2026, 8, 28, 4, 1, 0, TimeSpan.Zero)));
    }
}
