using GreyGray.Modules.Payment.Core;
using GreyGray.Modules.Payment.Infra;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.PaymentLedger.Tests;

/// <summary>BE-49：透過正式組合根驗證網址必填，不讓三鍵憑證默默使用測試站。</summary>
public sealed class EcpayConfigurationTests
{
    private const string CheckoutKey = "Payment:ECPay:CheckoutUrl";
    private const string CreditDetailKey = "Payment:ECPay:CreditDetailUrl";
    private const string CheckoutPath = "/Cashier/AioCheckOut/V5";
    private const string CreditDetailPath = "/CreditDetail/DoAction";

    [Fact(DisplayName = "只給三個憑證鍵，DI 解析拒絕並點名缺少 CheckoutUrl")]
    public void Three_credentials_without_urls_are_rejected()
    {
        var exception = Should.Throw<InvalidOperationException>(() => Resolve(Credentials()));
        AssertMissingUrl(exception, CheckoutKey, CheckoutPath);
    }

    [Fact(DisplayName = "三個憑證鍵加 CheckoutUrl，DI 解析仍拒絕缺少 CreditDetailUrl")]
    public void Credit_detail_url_is_required_independently()
    {
        var values = Credentials();
        values[CheckoutKey] = "https://payment.ecpay.com.tw" + CheckoutPath;
        var exception = Should.Throw<InvalidOperationException>(() => Resolve(values));
        AssertMissingUrl(exception, CreditDetailKey, CreditDetailPath);
    }

    [Theory(DisplayName = "任一端點是空字串或只有空白，都回缺設定而非網址格式錯誤")]
    [InlineData(CheckoutKey, "", CheckoutPath)]
    [InlineData(CheckoutKey, " \t ", CheckoutPath)]
    [InlineData(CreditDetailKey, "", CreditDetailPath)]
    [InlineData(CreditDetailKey, " \t ", CreditDetailPath)]
    public void Blank_urls_are_missing_configuration(string key, string value, string path)
    {
        var values = CompleteSettings("https://payment.ecpay.com.tw");
        values[key] = value;
        var exception = Should.Throw<InvalidOperationException>(() => Resolve(values));
        AssertMissingUrl(exception, key, path);
    }

    [Theory(DisplayName = "任一端點不是絕對網址，維持原本的格式錯誤")]
    [InlineData(CheckoutKey)]
    [InlineData(CreditDetailKey)]
    public void Relative_urls_keep_the_existing_error(string key)
    {
        var values = CompleteSettings("https://payment.ecpay.com.tw");
        values[key] = "relative/endpoint";
        var exception = Should.Throw<InvalidOperationException>(() => Resolve(values));
        exception.Message.ShouldBe($"{key} 必須是絕對網址。");
    }

    [Theory(DisplayName = "五鍵完整時明確選擇正式站或測試站，解析結果保留原網址")]
    [InlineData("https://payment.ecpay.com.tw")]
    [InlineData("https://payment-stage.ecpay.com.tw")]
    public void Explicit_ecpay_urls_are_preserved(string origin)
    {
        var settings = Resolve(CompleteSettings(origin));
        settings.CheckoutUrl.ShouldBe(new Uri(origin + CheckoutPath));
        settings.CreditDetailUrl.ShouldBe(new Uri(origin + CreditDetailPath));
    }

    [Theory(DisplayName = "未開模擬器旗標，任一端點使用 HTTP、非綠界或仿冒網域仍被守衛拒絕")]
    [InlineData(CheckoutKey, "http://payment.ecpay.com.tw")]
    [InlineData(CreditDetailKey, "http://payment.ecpay.com.tw")]
    [InlineData(CheckoutKey, "https://simulator.test")]
    [InlineData(CreditDetailKey, "https://simulator.test")]
    [InlineData(CheckoutKey, "https://ecpay.com.tw.evil.test")]
    [InlineData(CreditDetailKey, "https://ecpay.com.tw.evil.test")]
    public void Endpoint_guard_still_rejects_unsafe_urls(string key, string origin)
    {
        var values = CompleteSettings("https://payment.ecpay.com.tw");
        values[key] = origin + (key == CheckoutKey ? CheckoutPath : CreditDetailPath);
        var exception = Should.Throw<InvalidOperationException>(() => Resolve(values));
        exception.Message.ShouldContain(key);
        exception.Message.ShouldContain(values[key]!);
        exception.Message.ShouldContain("AllowNonEcpayEndpoints");
    }

    [Fact(DisplayName = "ADR-029：兩個模擬器網址加明確旗標，DI 解析保留原網址")]
    public void Explicit_simulator_urls_and_flag_remain_usable()
    {
        const string origin = "http://127.0.0.1:5009";
        var values = CompleteSettings(origin);
        values["Payment:ECPay:AllowNonEcpayEndpoints"] = "true";
        var settings = Resolve(values);
        settings.CheckoutUrl.ShouldBe(new Uri(origin + CheckoutPath));
        settings.CreditDetailUrl.ShouldBe(new Uri(origin + CreditDetailPath));
    }

    private static void AssertMissingUrl(InvalidOperationException exception, string key, string path)
    {
        exception.Message.ShouldContain($"缺少綠界設定 '{key}'");
        exception.Message.ShouldContain("https://payment.ecpay.com.tw" + path);
        exception.Message.ShouldContain("https://payment-stage.ecpay.com.tw" + path);
        exception.Message.ShouldContain("刻意沒有預設");
        exception.Message.ShouldContain("正式金鑰安靜地打到測試站");
    }

    private static Dictionary<string, string?> Credentials() => new()
    {
        ["Payment:ECPay:MerchantId"] = "DEVFAKE0000",
        ["Payment:ECPay:HashKey"] = "DEVFAKEHASHKEY01",
        ["Payment:ECPay:HashIV"] = "DEVFAKEHASHIV001",
    };

    private static Dictionary<string, string?> CompleteSettings(string origin)
    {
        var values = Credentials();
        values[CheckoutKey] = origin + CheckoutPath;
        values[CreditDetailKey] = origin + CreditDetailPath;
        return values;
    }

    private static EcpaySettings Resolve(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddPaymentModule(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        // 設定與 Gateway 都走正式 DI 工廠；不呼叫 HTTP，也不需要資料庫。
        var settings = scope.ServiceProvider.GetRequiredService<EcpaySettings>();
        scope.ServiceProvider.GetRequiredService<IEcpayGateway>().ShouldBeOfType<EcpayGateway>();
        return settings;
    }
}
