using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;
using StorefrontEndpoints = GreyGray.Api.Storefront.M1aEndpoints;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

/// <summary>
/// BE-42：綠界 <c>ReturnURL</c> 的組法。純函式，不需要資料庫。
/// </summary>
/// <remarks>
/// 為什麼值得一組測試：這條網址是<b>綠界的伺服器</b>要打回來的位址，錯了不會有任何例外，
/// 只會讓付款永遠停在待付款。正式機在 Cloudflare Tunnel 後面，<c>Request.Host</c> 是
/// <c>127.0.0.1:5000</c>，所以「有設定就用設定」這一條是正式機唯一能通的路；
/// 而 dev 的綠界模擬器跟 Host 同機，「沒設定就用請求」那一條是 dev 唯一能通的路。
/// 兩條都要守住，壞值一定要炸而不是悄悄退回第二條。
/// </remarks>
public sealed class EcpayReturnUrlTests
{
    private static IConfiguration Configuration(string? publicApiOrigin) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:PublicApiOrigin"] = publicApiOrigin,
            })
            .Build();

    private static HttpRequest Request(string scheme, string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        return context.Request;
    }

    [Fact]
    public void 有設定時用設定值組回呼網址()
    {
        var url = StorefrontEndpoints.BuildEcpayReturnUrl(
            Configuration("https://greygray.shop"),
            Request("http", "127.0.0.1:5000"));

        url.ToString().ShouldBe("https://greygray.shop/v1/webhooks/ecpay");
    }

    [Fact]
    public void 設定值的結尾斜線會被去掉()
    {
        var url = StorefrontEndpoints.BuildEcpayReturnUrl(
            Configuration("https://greygray.shop/"),
            Request("http", "127.0.0.1:5000"));

        url.ToString().ShouldBe("https://greygray.shop/v1/webhooks/ecpay");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 沒設定時退回這一次請求的scheme與host(string? origin)
    {
        var url = StorefrontEndpoints.BuildEcpayReturnUrl(
            Configuration(origin),
            Request("http", "127.0.0.1:5000"));

        url.ToString().ShouldBe("http://127.0.0.1:5000/v1/webhooks/ecpay");
    }

    [Theory]
    [InlineData("/v1")]
    [InlineData("greygray.shop")]
    [InlineData("ftp://greygray.shop")]
    public void 設定值不是絕對的http網址就丟例外且訊息含鍵名(string origin)
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            StorefrontEndpoints.BuildEcpayReturnUrl(
                Configuration(origin),
                Request("http", "127.0.0.1:5000")));

        exception.Message.ShouldContain("Storefront:PublicApiOrigin");
        exception.Message.ShouldContain(origin);
    }
}
