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
                new Uri("https://payment-stage.ecpay.com.tw/CreditDetail/DoAction"),
                TimeSpan.FromMinutes(30),
                TimeSpan.FromMinutes(20),
                false),
            "pwFHCqoQZGmho4w6",
            "EkRm7iFT261dpevs",
            new HttpClient());

        gateway.VerifyCallback(fields).ShouldBeTrue();
        fields["TotalAmount"] = "30001";
        gateway.VerifyCallback(fields).ShouldBeFalse();
    }

    /// <summary>
    /// 這組欄位與雜湊值曾在 2026-08-30 直接對綠界測試環境
    /// <c>https://payment-stage.ecpay.com.tw/CreditDetail/DoAction</c> 送過一次真實請求，
    /// 回應是 <c>RtnCode=0, RtnMsg=訂單不存在</c>（因為 TradeNo 是隨意填的），
    /// 而不是 <c>RtnCode=10200073, RtnMsg=CheckMacValue Error.</c>——
    /// 代表這組簽章確實通過了綠界伺服器的驗簽，只是業務層查無交易。
    /// 這條測試把當時驗證過的雜湊值釘住，之後不用每次都連外部網路。
    /// </summary>
    [Fact(DisplayName = "退刷欄位的 CheckMacValue 與已對綠界測試環境驗證過的值一致")]
    public void CheckMacValue_matches_a_verified_refund_request_against_ecpay_stage()
    {
        var fields = new Dictionary<string, string>
        {
            ["MerchantID"] = "2000132",
            ["MerchantTradeNo"] = "GGREFUNDTEST0001",
            ["TradeNo"] = "99999999999999",
            ["Action"] = "R",
            ["TotalAmount"] = "100",
        };

        EcpayGateway.ComputeCheckMacValue(fields, "5294y06JbISpM5x9", "v77hoKGq4kWxNNIS")
            .ShouldBe("408D3C2784AC0D9932B78527DD04D81A864F48103D5B3E154D961C5918705229");
    }

    /// <summary>
    /// 直接用正式的 <see cref="EcpayGateway"/>（不是另外手寫的探測腳本）對綠界測試環境
    /// 送一次真實的退刷請求。<b>刻意 Skip</b>——這條會連外部網路，不適合進 ops/test.ps1
    /// 的常態關卡（機器沒有網路，或綠界測試站掛掉，都不該讓全套測試變紅）。
    /// 2026-08-30 手動拿掉 Skip 執行過一次，回應是
    /// <c>RtnCode=0, RtnMsg=訂單不存在</c>——見 BE-17 交付說明。
    /// </summary>
    [Fact(
        DisplayName = "（手動）RequestRefundAsync 對綠界測試環境送出真實退刷請求",
        Skip = "會連外部網路（payment-stage.ecpay.com.tw），只在需要重新驗證串接時手動拿掉 Skip 執行。")]
    public async Task RequestRefundAsync_reaches_ecpay_stage_and_returns_a_real_response()
    {
        using var httpClient = new HttpClient();
        var gateway = new EcpayGateway(
            new EcpaySettings(
                "2000132",
                new Uri("https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5"),
                new Uri("https://payment-stage.ecpay.com.tw/CreditDetail/DoAction"),
                TimeSpan.FromMinutes(30),
                TimeSpan.FromMinutes(20),
                false),
            "5294y06JbISpM5x9",
            "v77hoKGq4kWxNNIS",
            httpClient);

        var result = await gateway.RequestRefundAsync(
            "GGREFUNDTEST0001",
            "99999999999999",
            Money.OfMajor(1, Currency.TWD),
            TestContext.Current.CancellationToken);

        // 綠界官方文件明講測試環境「因無法提供實際授權，故無法使用此 API」——
        // 這裡不斷言 Succeeded，只斷言「有連上、簽章有過」：RtnCode 不是簽章錯誤代碼。
        result.RtnCode.ShouldNotBe("10200073");
        result.RawResponse.ShouldNotBeNullOrWhiteSpace();
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
