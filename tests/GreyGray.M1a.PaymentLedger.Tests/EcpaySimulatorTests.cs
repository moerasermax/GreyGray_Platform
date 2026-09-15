using System.Net;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Payment.Core;
using GreyGray.Modules.Payment.Infra;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using GreyGray.Tools.EcpaySimulator.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using PaymentEntity = GreyGray.Modules.Payment.Core.Payment;

namespace GreyGray.M1a.PaymentLedger.Tests;

/// <summary>
/// dev 綠界模擬器（ADR-029）＋ <c>ClientBackURL</c>（#33）＋ 非綠界網域守衛的斷言。
/// </summary>
/// <remarks>
/// <b>這裡沒有任何一個假的 <see cref="IEcpayGateway"/>。</b>
/// 模擬器組出來的東西一律餵給<b>真的</b> <see cref="EcpayGateway"/> 與
/// <see cref="PaymentApplicationService"/>——「模擬器過了」要等於「現在這一套判斷過了」，
/// 否則它就只是在假綠燈上蓋章。
/// </remarks>
public sealed class EcpaySimulatorTests
{
    private const string FakeMerchantId = "DEVFAKE0000";
    private const string FakeHashKey = "DEVFAKEHASHKEY01";
    private const string FakeHashIv = "DEVFAKEHASHIV001";

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 4, 0, 0, TimeSpan.Zero);

    private static readonly Uri SimulatorCheckoutUrl =
        new("http://127.0.0.1:5009/Cashier/AioCheckOut/V5");

    private static readonly Uri SimulatorCreditDetailUrl =
        new("http://127.0.0.1:5009/CreditDetail/DoAction");

    [Fact(DisplayName = "模擬器的付款結果通知通過真的驗簽，並讓真的回呼處理把付款轉成 Captured")]
    public async Task Simulator_notification_captures_the_payment_through_production_code()
    {
        var (service, repository, publisher, gateway) = CreateService();
        var checkout = await InitiateAsync(service);

        EcpaySimulatorCore.ValidateCheckoutForm(checkout, FakeMerchantId, FakeHashKey, FakeHashIv)
            .IsValid.ShouldBeTrue();

        var notification = BuildNotification(checkout, SimulatedOutcome.Success);
        gateway.VerifyCallback(notification).ShouldBeTrue();

        var result = await service.HandleEcpayCallbackAsync(notification, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var payment = repository.Items.ShouldHaveSingleItem();
        payment.Status.ShouldBe(PaymentStatus.Captured);
        payment.ProviderTransactionId.ShouldNotBeNull();
        payment.ProviderTransactionId.ShouldStartWith(EcpaySimulatorCore.FakePrefix);
        publisher.Events.ShouldHaveSingleItem().ShouldBeOfType<PaymentCaptured>();
    }

    [Fact(DisplayName = "模擬器發的 TradeNo 是 DEVFAKE ＋ 13 位數字、共 20 字")]
    public void Simulated_trade_number_is_recognisable_at_a_glance()
    {
        var tradeNo = EcpaySimulatorCore.CreateTradeNo(
            new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.FromHours(8)),
            7);

        tradeNo.ShouldBe("DEVFAKE2609021200007");
        tradeNo.Length.ShouldBe(20);
        tradeNo.ShouldStartWith(EcpaySimulatorCore.FakePrefix);
        tradeNo[EcpaySimulatorCore.FakePrefix.Length..].ShouldAllBe(character => char.IsAsciiDigit(character));
    }

    [Fact(DisplayName = "通知裡的 TradeAmt 被竄改就驗不過（先紅後綠的那一條）")]
    public async Task Tampering_with_the_trade_amount_fails_signature_verification()
    {
        var (service, _, publisher, gateway) = CreateService();
        var checkout = await InitiateAsync(service);
        var notification = BuildNotification(checkout, SimulatedOutcome.Success);
        notification["TradeAmt"] = "1";

        gateway.VerifyCallback(notification).ShouldBeFalse();
        var result = await service.HandleEcpayCallbackAsync(notification, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("payment.invalid-signature");
        publisher.Events.ShouldBeEmpty();
    }

    [Fact(DisplayName = "「模擬付款失敗」走的是正式那條失敗路徑：Failed ＋ PaymentFailed")]
    public async Task Simulated_failure_goes_down_the_production_failure_path()
    {
        var (service, repository, publisher, _) = CreateService();
        var checkout = await InitiateAsync(service);
        var notification = BuildNotification(checkout, SimulatedOutcome.Failure);

        // 刻意不送 SimulatePaid=1：那是綠界測試站的旗標，會走到 AllowSimulatedPaid 特例。
        notification["SimulatePaid"].ShouldBe("0");

        var result = await service.HandleEcpayCallbackAsync(notification, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        repository.Items.ShouldHaveSingleItem().Status.ShouldBe(PaymentStatus.Failed);
        var failed = publisher.Events.ShouldHaveSingleItem().ShouldBeOfType<PaymentFailed>();
        failed.FailureCode.ShouldBe(EcpaySimulatorCore.FailureRtnCode);
    }

    [Fact(DisplayName = "模擬器的 DoAction 回應餵給真的 RequestRefundAsync 會讀成退刷成功")]
    public async Task DoAction_response_is_understood_by_the_real_refund_client()
    {
        var handler = new SimulatorRefundHandler();
        using var httpClient = new HttpClient(handler);
        var gateway = new EcpayGateway(Settings(), FakeHashKey, FakeHashIv, httpClient);

        var result = await gateway.RequestRefundAsync(
            "GG0000000000000000A1",
            "DEVFAKE2609021200007",
            Money.OfMajor(160, Currency.TWD),
            TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();
        result.RtnCode.ShouldBe("1");
        result.RtnMsg.ShouldBe("OK");
        handler.RequestUri.ShouldBe(SimulatorCreditDetailUrl);
    }

    [Fact(DisplayName = "模擬器拒絕簽錯的結帳表單，錯誤碼是綠界的 10200073")]
    public void Simulator_rejects_a_mis_signed_checkout_form()
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MerchantID"] = FakeMerchantId,
            ["MerchantTradeNo"] = "GG0000000000000000A1",
            ["MerchantTradeDate"] = "2026/09/02 12:00:00",
            ["TotalAmount"] = "160",
            ["ReturnURL"] = "http://127.0.0.1:5000/v1/webhooks/ecpay",
            ["CheckMacValue"] = new string('0', 64),
        };

        var validation = EcpaySimulatorCore.ValidateCheckoutForm(
            fields, FakeMerchantId, FakeHashKey, FakeHashIv);

        validation.IsValid.ShouldBeFalse();
        validation.RtnCode.ShouldBe(EcpaySimulatorCore.CheckMacValueErrorCode);
        validation.RtnMsg.ShouldBe(EcpaySimulatorCore.CheckMacValueErrorMessage);
    }

    [Fact(DisplayName = "必做 1：結帳欄位含 ClientBackURL，而且它有進簽章")]
    public void Checkout_fields_carry_client_back_url_inside_the_signature()
    {
        var gateway = new EcpayGateway(Settings(), FakeHashKey, FakeHashIv, new HttpClient());
        var clientBackUrl = new Uri("http://127.0.0.1:5002/payment/result?orderId=deadbeef");

        var fields = gateway.CreateCheckoutFields(
            "GG0000000000000000A1",
            Money.OfMajor(160, Currency.TWD),
            "GreyGray GG-20260902-0001",
            new Uri("http://127.0.0.1:5000/v1/webhooks/ecpay"),
            clientBackUrl,
            Now);

        fields.ShouldContainKey("ClientBackURL");
        fields["ClientBackURL"].ShouldBe(clientBackUrl.AbsoluteUri);
        gateway.VerifyCallback(fields).ShouldBeTrue();

        // 「有進簽章」＝ 改掉它就驗不過。少了這一句，欄位可以被中途換掉而沒人發現。
        var tampered = fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        tampered["ClientBackURL"] = "http://evil.test/payment/result";
        gateway.VerifyCallback(tampered).ShouldBeFalse();
    }

    [Fact(DisplayName = "必做 2：不開旗標時，非綠界網域的端點在 DI 解析期就被拒絕")]
    public void Non_ecpay_endpoints_are_rejected_unless_the_flag_is_set()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => ResolveGateway(new Dictionary<string, string?>
            {
                ["Payment:ECPay:CheckoutUrl"] = SimulatorCheckoutUrl.AbsoluteUri,
                ["Payment:ECPay:CreditDetailUrl"] = SimulatorCreditDetailUrl.AbsoluteUri,
            }));

        exception.Message.ShouldContain("Payment:ECPay:CheckoutUrl");
        exception.Message.ShouldContain(SimulatorCheckoutUrl.AbsoluteUri);
        exception.Message.ShouldContain("AllowNonEcpayEndpoints");
    }

    [Fact(DisplayName = "必做 2：明確開了 AllowNonEcpayEndpoints 才放行任意端點")]
    public void The_flag_opens_the_door_for_the_local_simulator()
    {
        Should.NotThrow(() => ResolveGateway(new Dictionary<string, string?>
        {
            ["Payment:ECPay:CheckoutUrl"] = SimulatorCheckoutUrl.AbsoluteUri,
            ["Payment:ECPay:CreditDetailUrl"] = SimulatorCreditDetailUrl.AbsoluteUri,
            ["Payment:ECPay:AllowNonEcpayEndpoints"] = "true",
        }));
    }

    [Fact(DisplayName = "必做 2：明確填入綠界 stage 的兩個網址時不開旗標也過")]
    public void Explicit_stage_urls_pass_without_the_flag()
        => Should.NotThrow(() => ResolveGateway([]));

    [Fact(DisplayName = "必做 2：https 的 ecpay.com.tw 子網域放行，http 或別的網域不放行")]
    public void The_guard_only_accepts_https_ecpay_hosts()
    {
        Should.NotThrow(() => ResolveGateway(new Dictionary<string, string?>
        {
            ["Payment:ECPay:CheckoutUrl"] = "https://payment.ecpay.com.tw/Cashier/AioCheckOut/V5",
            ["Payment:ECPay:CreditDetailUrl"] = "https://payment.ecpay.com.tw/CreditDetail/DoAction",
        }));

        // http 的綠界網域也擋——回呼與退刷都帶得動錢，明文傳輸沒有理由放行。
        Should.Throw<InvalidOperationException>(() => ResolveGateway(new Dictionary<string, string?>
        {
            ["Payment:ECPay:CheckoutUrl"] = "http://payment.ecpay.com.tw/Cashier/AioCheckOut/V5",
        }));

        // 「結尾像 ecpay.com.tw」的仿冒網域擋得掉：ecpay.com.tw.evil.test 不以 .ecpay.com.tw 結尾。
        Should.Throw<InvalidOperationException>(() => ResolveGateway(new Dictionary<string, string?>
        {
            ["Payment:ECPay:CheckoutUrl"] = "https://ecpay.com.tw.evil.test/Cashier/AioCheckOut/V5",
        }));
    }

    [Fact(DisplayName = "電子地圖表單通過後會組出九個同形回傳欄位，包含離島旗標")]
    public void Cvs_map_builds_the_ecpay_reply_shape()
    {
        var form = ValidCvsMapForm();
        EcpaySimulatorCore.ValidateCvsMapForm(form).IsValid.ShouldBeTrue();

        var islandStore = EcpaySimulatorCore.FakeStores.Single(store => store.IsOutlying);
        var reply = EcpaySimulatorCore.BuildCvsMapReplyFields(form, islandStore);

        reply.Count.ShouldBe(9);
        reply["MerchantID"].ShouldBe(FakeMerchantId);
        reply["LogisticsSubType"].ShouldBe("UNIMARTC2C");
        reply["CVSStoreID"].ShouldBe(islandStore.Code);
        reply["CVSOutSide"].ShouldBe("1");
        reply["ExtraData"].ShouldBe(form["ExtraData"]);
    }

    [Theory(DisplayName = "電子地圖表單缺欄、錯物流型別、壞網址或 ExtraData 超長都會被明確拒絕")]
    [InlineData("MerchantID", "REAL1234", "MerchantID")]
    [InlineData("LogisticsType", "HOME", "LogisticsType")]
    [InlineData("LogisticsSubType", "FAMIC2C", "LogisticsSubType")]
    [InlineData("ServerReplyURL", "/reply", "ServerReplyURL")]
    [InlineData("ServerReplyURL", "ftp://example.test/reply", "ServerReplyURL")]
    [InlineData("ExtraData", "", "ExtraData")]
    [InlineData("ExtraData", "123456789012345678901", "ExtraData")]
    public void Invalid_cvs_map_fields_are_rejected(
        string key,
        string value,
        string expectedMessage)
    {
        var form = ValidCvsMapForm();
        form[key] = value;

        var validation = EcpaySimulatorCore.ValidateCvsMapForm(form);

        validation.IsValid.ShouldBeFalse();
        validation.ErrorMessage.ShouldContain(expectedMessage);
    }

    [Fact(DisplayName = "假地圖 HTML 對網址、票與門市欄位全部跳脫，注入字串不會原樣出現")]
    public void Cvs_map_html_encodes_every_external_value()
    {
        const string injection = "\"><script>alert('x')</script><input onfocus='x'";
        var form = ValidCvsMapForm();
        form["ServerReplyURL"] = $"https://example.test/{injection}";
        form["ExtraData"] = injection;
        var stores = new[]
        {
            new CvsMapStore(injection, injection, injection, injection, false),
        };

        var html = EcpaySimulatorCore.RenderCvsMapPage(form, stores);

        html.ShouldNotContain(injection);
        html.ShouldNotContain("<script>alert('x')</script>");
        html.ShouldNotContain("onfocus='x'");
        html.ShouldContain("&quot;&gt;&lt;script&gt;");
        html.ShouldContain("偽造回傳：MerchantID 錯");
    }

    // ── 樁與工具 ─────────────────────────────────────────────────────────

    private static EcpaySettings Settings() => new(
        FakeMerchantId,
        SimulatorCheckoutUrl,
        SimulatorCreditDetailUrl,
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(20),
        AllowSimulatedPaid: false);

    private static Dictionary<string, string> ValidCvsMapForm() =>
        new(StringComparer.Ordinal)
        {
            ["MerchantID"] = FakeMerchantId,
            ["LogisticsType"] = "CVS",
            ["LogisticsSubType"] = "UNIMARTC2C",
            ["IsCollection"] = "N",
            ["ServerReplyURL"] = "http://127.0.0.1:5000/v1/logistics/cvs-map/reply",
            ["ExtraData"] = "AbCdEf0123456789GhIj",
        };

    private static (
        PaymentApplicationService Service,
        StubRepository Repository,
        RecordingPublisher Publisher,
        EcpayGateway Gateway) CreateService()
    {
        var repository = new StubRepository();
        var publisher = new RecordingPublisher();
        var gateway = new EcpayGateway(Settings(), FakeHashKey, FakeHashIv, new HttpClient());
        var service = new PaymentApplicationService(
            repository,
            new NoopUnitOfWork(),
            publisher,
            gateway,
            Settings(),
            new StubClock(Now),
            new StubCorrelationContext());
        return (service, repository, publisher, gateway);
    }

    private static async Task<IReadOnlyDictionary<string, string>> InitiateAsync(
        PaymentApplicationService service)
    {
        var initiation = await service.InitiateAsync(
            new PaymentInitiationRequest(
                OrderId.New(),
                Money.OfMajor(100, Currency.TWD),
                Money.OfMajor(60, Currency.TWD),
                "GreyGray GG-20260902-0001",
                new Uri("http://127.0.0.1:5000/v1/webhooks/ecpay"),
                new Uri("http://127.0.0.1:5002/payment/result?orderId=deadbeef")),
            TestContext.Current.CancellationToken);

        initiation.IsSuccess.ShouldBeTrue();
        initiation.Value.Action.ShouldBe(SimulatorCheckoutUrl);
        return initiation.Value.Fields;
    }

    private static Dictionary<string, string> BuildNotification(
        IReadOnlyDictionary<string, string> checkout,
        SimulatedOutcome outcome)
    {
        var taipeiNow = EcpaySimulatorCore.ToTaipei(Now);
        var notification = EcpaySimulatorCore.BuildPaymentNotification(
            checkout,
            outcome,
            EcpaySimulatorCore.CreateTradeNo(taipeiNow, 3),
            taipeiNow,
            FakeHashKey,
            FakeHashIv);
        return notification.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// 走真正的組合根（<c>AddPaymentModule</c>）解析 <see cref="IEcpayGateway"/>，
    /// 才驗得到守衛真的長在正式碼的設定讀取路徑上，而不是測試自己另外寫的一份判斷。
    /// </summary>
    private static void ResolveGateway(Dictionary<string, string?> overrides)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Payment:ECPay:MerchantId"] = FakeMerchantId,
            ["Payment:ECPay:HashKey"] = FakeHashKey,
            ["Payment:ECPay:HashIV"] = FakeHashIv,
            ["Payment:ECPay:CheckoutUrl"] = "https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5",
            ["Payment:ECPay:CreditDetailUrl"] = "https://payment-stage.ecpay.com.tw/CreditDetail/DoAction",
        };
        foreach (var pair in overrides)
        {
            settings[pair.Key] = pair.Value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddPaymentModule(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IEcpayGateway>();
    }

    /// <summary>把送出去的退刷請求交給模擬器的純函式，再把它的回應當成 HTTP 回應送回來。</summary>
    private sealed class SimulatorRefundHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            var raw = await request.Content!.ReadAsStringAsync(cancellationToken);
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                fields[Uri.UnescapeDataString(parts[0])] =
                    parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
            }

            var body = EcpaySimulatorCore.BuildDoActionResponse(fields, FakeHashKey, FakeHashIv);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            };
        }
    }

    private sealed class StubRepository : IPaymentRepository
    {
        public List<PaymentEntity> Items { get; } = [];

        public void Add(PaymentEntity payment) => Items.Add(payment);

        public Task<PaymentEntity?> FindByOrderAsync(
            TenantId tenantId,
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(payment => payment.OrderId == orderId));

        public Task<PaymentEntity?> FindByMerchantTradeNoAsync(
            TenantId tenantId,
            string merchantTradeNo,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(payment =>
                StringComparer.Ordinal.Equals(payment.MerchantTradeNo, merchantTradeNo)));

        public Task<PaymentEntity?> FindByIdAsync(
            TenantId tenantId,
            PaymentId id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(payment => payment.Id == id));

        public Task<IReadOnlyList<PaymentEntity>> FindByOrderAllAsync(
            TenantId tenantId,
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PaymentEntity>>(
                Items.Where(payment => payment.OrderId == orderId).ToArray());

        public Task<PaymentEntity?> FindCapturedOrRefundedByOrderAsync(
            TenantId tenantId,
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(payment =>
                payment.OrderId == orderId &&
                payment.Status is PaymentStatus.Captured
                    or PaymentStatus.PartiallyRefunded
                    or PaymentStatus.Refunded));
    }

    private sealed class NoopUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<object> Events { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class StubClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;

        public DateOnly TodayInTaipei => DateOnly.FromDateTime(now.UtcDateTime.AddHours(8));
    }

    private sealed class StubCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "00000000000000000000000000000001";

        public string? CausationId => "0000000000000001";

        public TenantId TenantId => GreyGray.Shared.Kernel.TenantId.Default;
    }
}
