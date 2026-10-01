using System.Globalization;
using System.Reflection;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Payment.Core;
using GreyGray.Modules.Payment.Infra;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Shouldly;
using Xunit;
using PaymentEntity = GreyGray.Modules.Payment.Core.Payment;

namespace GreyGray.M1a.PaymentLedger.Tests;

public sealed class PaymentInstructionsTests
{
    private const string MerchantId = "BE62MERCHANT";
    private const string HashKey = "BE62HASHKEY00001";
    private const string HashIv = "BE62HASHIV000001";
    private const string TradeNo = "2610011200000001";
    private static readonly Uri PaymentInfoUrl = new("https://api.example.test/payment-info");
    private static readonly DateTimeOffset InitialNow =
        new(2026, 10, 1, 4, 0, 0, TimeSpan.Zero);

    [Theory(DisplayName = "P1/P2 非即時付款發動會送單一方式、期限與 PaymentInfoURL")]
    [InlineData(PaymentMethod.Atm, "ATM", "ExpireDate", "3")]
    [InlineData(PaymentMethod.ConvenienceStoreCode, "CVS", "StoreExpireDate", "4320")]
    [InlineData(PaymentMethod.Barcode, "BARCODE", "StoreExpireDate", "3")]
    public async Task P1_P2_deferred_checkout_fields_are_method_specific(
        PaymentMethod method,
        string choosePayment,
        string expiryKey,
        string expiryValue)
    {
        var harness = CreateHarness();

        var payment = await harness.InitiateAsync(method, PaymentInfoUrl);
        var fields = harness.LastInitiation!.Fields;

        payment.Method.ShouldBe(method);
        fields["ChoosePayment"].ShouldBe(choosePayment);
        fields["PaymentInfoURL"].ShouldBe(PaymentInfoUrl.AbsoluteUri);
        fields[expiryKey].ShouldBe(expiryValue);
    }

    [Fact(DisplayName = "P3 信用卡 checkout 的 12 鍵逐鍵維持既有值")]
    public async Task P3_credit_card_checkout_fields_remain_exact()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync();
        var actual = harness.LastInitiation!.Fields;
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MerchantID"] = MerchantId,
            ["MerchantTradeNo"] = payment.MerchantTradeNo,
            ["MerchantTradeDate"] = "2026/10/01 12:00:00",
            ["PaymentType"] = "aio",
            ["TotalAmount"] = "160",
            ["TradeDesc"] = "BE-62 payment",
            ["ItemName"] = "BE-62 payment",
            ["ReturnURL"] = "https://api.example.test/ecpay-result",
            ["ClientBackURL"] = "https://shop.example.test/payment-result",
            ["ChoosePayment"] = "Credit",
            ["EncryptType"] = "1",
        };
        expected["CheckMacValue"] = EcpayGateway.ComputeCheckMacValue(expected, HashKey, HashIv);

        actual.Count.ShouldBe(12);
        foreach (var pair in expected)
        {
            actual[pair.Key].ShouldBe(pair.Value);
        }
    }

    [Fact(DisplayName = "P4 非信用卡缺 PaymentInfoUrl 回業務失敗且不寫資料")]
    public async Task P4_deferred_payment_requires_payment_info_url()
    {
        var harness = CreateHarness();

        var result = await harness.InitiateResultAsync(PaymentMethod.Atm, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("payment.payment-info-url-required");
        harness.Repository.Items.ShouldBeEmpty();
        harness.UnitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact(DisplayName = "P5 未過期信用卡改用 ATM，舊筆作廢後建立新單號")]
    public async Task P5_changing_method_supersedes_pending_attempt()
    {
        var harness = CreateHarness();
        var oldPayment = await harness.InitiateAsync();

        var newPayment = await harness.InitiateAsync(PaymentMethod.Atm, PaymentInfoUrl);

        oldPayment.Status.ShouldBe(PaymentStatus.Failed);
        newPayment.Status.ShouldBe(PaymentStatus.Pending);
        newPayment.Method.ShouldBe(PaymentMethod.Atm);
        newPayment.MerchantTradeNo.ShouldNotBe(oldPayment.MerchantTradeNo);
        harness.LastInitiation!.Fields["ChoosePayment"].ShouldBe("ATM");
    }

    [Fact(DisplayName = "I1 ATM 取號用日期當天 23:59:59 台北時間，且不套 20 分鐘窗")]
    public async Task I1_atm_instructions_use_taipei_end_of_day()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync(PaymentMethod.Atm, PaymentInfoUrl);
        var info = harness.BuildInfo(payment, PaymentMethod.Atm, tradeDate: InitialNow.AddHours(-2));

        var result = await harness.Service.HandleEcpayPaymentInfoAsync(info, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        info.ContainsKey("PaymentDate").ShouldBeFalse();
        payment.Status.ShouldBe(PaymentStatus.InstructionsIssued);
        payment.ProviderExpiresAt.ShouldBe(new DateTimeOffset(2026, 10, 4, 15, 59, 59, TimeSpan.Zero));
        harness.Publisher.Events.ShouldHaveSingleItem().ShouldBeOfType<PaymentInstructionsIssued>();
    }

    [Theory(DisplayName = "I2/I3 CVS 與條碼取號正規化 UTC 並保存方式專屬欄位")]
    [InlineData(PaymentMethod.ConvenienceStoreCode)]
    [InlineData(PaymentMethod.Barcode)]
    public async Task I2_I3_store_instructions_are_preserved(PaymentMethod method)
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync(method, PaymentInfoUrl);

        var result = await harness.Service.HandleEcpayPaymentInfoAsync(
            harness.BuildInfo(payment, method),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        payment.ProviderExpiresAt.ShouldBe(new DateTimeOffset(2026, 10, 4, 12, 15, 0, TimeSpan.Zero));
        if (method == PaymentMethod.ConvenienceStoreCode)
        {
            payment.PaymentNo.ShouldBe("CVS1234567890");
        }
        else
        {
            new[] { payment.Barcode1, payment.Barcode2, payment.Barcode3 }
                .ShouldBe(["BARCODE-1", "BARCODE-2", "BARCODE-3"]);
        }
    }

    [Fact(DisplayName = "I4 同一取號通知重送成功且只發一次事件")]
    public async Task I4_instruction_notification_is_idempotent()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync(PaymentMethod.Atm, PaymentInfoUrl);
        var info = harness.BuildInfo(payment, PaymentMethod.Atm);

        (await harness.Service.HandleEcpayPaymentInfoAsync(info, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var firstSummary = payment.ToSummary();
        (await harness.Service.HandleEcpayPaymentInfoAsync(info, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        payment.ToSummary().ShouldBe(firstSummary);
        harness.Publisher.Events.Count.ShouldBe(1);
    }

    [Fact(DisplayName = "I5 取號失敗轉 Failed、發 PaymentFailed，之後可重新發動")]
    public async Task I5_failed_instruction_attempt_can_be_reinitiated()
    {
        var harness = CreateHarness();
        var failedPayment = await harness.InitiateAsync(PaymentMethod.Atm, PaymentInfoUrl);
        var failedInfo = harness.BuildInfo(failedPayment, PaymentMethod.Atm, rtnCode: 0);

        (await harness.Service.HandleEcpayPaymentInfoAsync(failedInfo, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        var replacement = await harness.InitiateAsync(PaymentMethod.Atm, PaymentInfoUrl);

        failedPayment.Status.ShouldBe(PaymentStatus.Failed);
        replacement.Status.ShouldBe(PaymentStatus.Pending);
        replacement.MerchantTradeNo.ShouldNotBe(failedPayment.MerchantTradeNo);
        harness.Publisher.Events.ShouldHaveSingleItem().ShouldBeOfType<PaymentFailed>();
    }

    [Theory(DisplayName = "I6/I7 付款方式不符或專屬欄位缺漏時不改資料")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task I6_I7_invalid_instruction_notification_does_not_mutate(bool mismatchedMethod)
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync(PaymentMethod.Atm, PaymentInfoUrl);
        var info = harness.BuildInfo(payment, PaymentMethod.Atm);
        if (mismatchedMethod)
        {
            info["PaymentType"] = "CVS_CVS";
        }
        else
        {
            info["vAccount"] = " ";
        }
        harness.Resign(info);

        var result = await harness.Service.HandleEcpayPaymentInfoAsync(info, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(mismatchedMethod
            ? "payment.payment-method-mismatch"
            : "payment.invalid-payment-instructions");
        payment.Status.ShouldBe(PaymentStatus.Pending);
        harness.Publisher.Events.ShouldBeEmpty();
    }

    [Fact(DisplayName = "I8 已被取代的付款收到取號通知回 attempt-superseded")]
    public async Task I8_superseded_attempt_rejects_instructions()
    {
        var harness = CreateHarness();
        var oldPayment = await harness.InitiateAsync(PaymentMethod.Atm, PaymentInfoUrl);
        await harness.InitiateAsync(PaymentMethod.Barcode, PaymentInfoUrl);

        var result = await harness.Service.HandleEcpayPaymentInfoAsync(
            harness.BuildInfo(oldPayment, PaymentMethod.Atm),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("payment.attempt-superseded");
        oldPayment.Status.ShouldBe(PaymentStatus.Failed);
        harness.Publisher.Events.ShouldBeEmpty();
    }

    [Fact(DisplayName = "C1′/C2′ 已取號付款不套時間窗，且要求相同 TradeNo")]
    public async Task C1_C2_captured_instruction_requires_same_trade_number()
    {
        var harness = CreateHarness();
        var payment = await harness.IssueAsync(PaymentMethod.Atm);
        var mismatch = harness.BuildPayment(payment, PaymentMethod.Atm, "OTHER-TRADE", InitialNow.AddDays(-2));
        var rejected = await harness.Service.HandleEcpayCallbackAsync(mismatch, CancellationToken.None);
        rejected.IsFailure.ShouldBeTrue();
        rejected.Error.Code.ShouldBe("payment.instructions-trade-no-mismatch");

        var result = await harness.Service.HandleEcpayCallbackAsync(
            harness.BuildPayment(payment, PaymentMethod.Atm, TradeNo, InitialNow.AddDays(-2)),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        payment.Status.ShouldBe(PaymentStatus.Captured);
        harness.Publisher.Events.Count(@event => @event is PaymentCaptured).ShouldBe(1);
    }

    [Fact(DisplayName = "C3′ 先付款成功再收到取號通知，維持 Captured 且不發取號事件")]
    public async Task C3_payment_before_instructions_stays_captured()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync(PaymentMethod.Atm, PaymentInfoUrl);
        (await harness.Service.HandleEcpayCallbackAsync(
            harness.BuildPayment(payment, PaymentMethod.Atm, TradeNo, InitialNow.AddDays(-2)),
            CancellationToken.None)).IsSuccess.ShouldBeTrue();

        var result = await harness.Service.HandleEcpayPaymentInfoAsync(
            harness.BuildInfo(payment, PaymentMethod.Atm),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        payment.Status.ShouldBe(PaymentStatus.Captured);
        harness.Publisher.Events.ShouldHaveSingleItem().ShouldBeOfType<PaymentCaptured>();
    }

    [Fact(DisplayName = "C4′ ATM 取號通知遺失時，付款結果可直接 Captured")]
    public async Task C4_atm_payment_can_capture_pending_without_instructions()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync(PaymentMethod.Atm, PaymentInfoUrl);

        var result = await harness.Service.HandleEcpayCallbackAsync(
            harness.BuildPayment(payment, PaymentMethod.Atm, TradeNo, InitialNow.AddDays(-2)),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        payment.Status.ShouldBe(PaymentStatus.Captured);
    }

    [Fact(DisplayName = "C5′ 舊資料 Method=null 依 PaymentType 判斷非即時付款，不套時間窗")]
    public async Task C5_legacy_null_method_uses_callback_payment_type()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync(PaymentMethod.Atm, PaymentInfoUrl);
        typeof(PaymentEntity).GetProperty(nameof(PaymentEntity.Method), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(payment, null);

        var result = await harness.Service.HandleEcpayCallbackAsync(
            harness.BuildPayment(payment, PaymentMethod.Atm, TradeNo, InitialNow.AddDays(-2)),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        payment.Status.ShouldBe(PaymentStatus.Captured);
    }

    [Fact(DisplayName = "C6′ 信用卡付款收到 ATM PaymentType 回 method-mismatch")]
    public async Task C6_credit_payment_rejects_atm_callback()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync();

        var result = await harness.Service.HandleEcpayCallbackAsync(
            harness.BuildPayment(payment, PaymentMethod.Atm, TradeNo, InitialNow),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("payment.payment-method-mismatch");
        payment.Status.ShouldBe(PaymentStatus.Pending);
    }

    [Theory(DisplayName = "C7′ ATM／CVS 取號成功碼送付款結果路由都回 unexpected-payment-info")]
    [InlineData(PaymentMethod.Atm)]
    [InlineData(PaymentMethod.ConvenienceStoreCode)]
    public async Task C7_payment_result_route_rejects_instruction_code(PaymentMethod method)
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync(method, PaymentInfoUrl);

        var result = await harness.Service.HandleEcpayCallbackAsync(
            harness.BuildInfo(payment, method),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("payment.unexpected-payment-info");
        payment.Status.ShouldBe(PaymentStatus.Pending);
    }

    [Fact(DisplayName = "R1/R2/R3 已取號重啟以 provider 期限加一秒為邊界")]
    public async Task R1_R2_R3_reinitiation_uses_provider_expiry_boundary()
    {
        var beforeExpiry = CreateHarness();
        var beforePayment = await beforeExpiry.IssueAsync(PaymentMethod.Atm);
        beforeExpiry.Clock.UtcNow = InitialNow.AddMinutes(31);
        (await beforeExpiry.InitiateResultAsync(PaymentMethod.Atm, PaymentInfoUrl))
            .Error.Code.ShouldBe("payment.instructions-already-issued");
        beforeExpiry.Repository.Items.ShouldHaveSingleItem().ShouldBeSameAs(beforePayment);

        var halfSecond = CreateHarness();
        var halfPayment = await halfSecond.IssueAsync(PaymentMethod.Atm);
        halfSecond.Clock.UtcNow = halfPayment.ProviderExpiresAt!.Value.AddMilliseconds(500);
        (await halfSecond.InitiateResultAsync(PaymentMethod.Atm, PaymentInfoUrl))
            .Error.Code.ShouldBe("payment.instructions-already-issued");

        var expired = CreateHarness();
        var oldPayment = await expired.IssueAsync(PaymentMethod.Atm);
        expired.Clock.UtcNow = oldPayment.ProviderExpiresAt!.Value.AddSeconds(1);
        var replacement = await expired.InitiateAsync(PaymentMethod.Atm, PaymentInfoUrl);
        oldPayment.Status.ShouldBe(PaymentStatus.Failed);
        replacement.MerchantTradeNo.ShouldNotBe(oldPayment.MerchantTradeNo);
    }

    [Fact(DisplayName = "R4 被取代付款的晚到成功通知可重送且不發 PaymentCaptured")]
    public async Task R4_late_capture_is_recorded_idempotently()
    {
        var harness = CreateHarness();
        var oldPayment = await harness.InitiateAsync();
        await harness.InitiateAsync(PaymentMethod.Atm, PaymentInfoUrl);
        var callback = harness.BuildPayment(oldPayment, PaymentMethod.CreditCard, TradeNo, InitialNow);

        (await harness.Service.HandleEcpayCallbackAsync(callback, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var firstRecordedAt = oldPayment.LateCapturedAt;
        (await harness.Service.HandleEcpayCallbackAsync(callback, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        oldPayment.Status.ShouldBe(PaymentStatus.Failed);
        oldPayment.LateCaptureTradeNo.ShouldBe(TradeNo);
        oldPayment.LateCapturedAt.ShouldBe(firstRecordedAt);
        harness.Publisher.Events.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Q1 只回傳狀態 5 且仍在期限內的取號資訊")]
    public async Task Q1_outstanding_query_filters_status_and_expiry()
    {
        var issued = CreateHarness();
        var issuedPayment = await issued.IssueAsync(PaymentMethod.Atm);
        (await issued.Service.GetOutstandingInstructionsAsync(issued.OrderId, CancellationToken.None))
            .Value.ShouldNotBeNull();
        issued.Clock.UtcNow = issuedPayment.ProviderExpiresAt!.Value.AddSeconds(1);
        (await issued.Service.GetOutstandingInstructionsAsync(issued.OrderId, CancellationToken.None))
            .Value.ShouldBeNull();

        var pendingCredit = CreateHarness();
        await pendingCredit.InitiateAsync();
        (await pendingCredit.Service.GetOutstandingInstructionsAsync(pendingCredit.OrderId, CancellationToken.None))
            .Value.ShouldBeNull();

        var captured = CreateHarness();
        var capturedPayment = await captured.IssueAsync(PaymentMethod.Atm);
        await captured.Service.HandleEcpayCallbackAsync(
            captured.BuildPayment(capturedPayment, PaymentMethod.Atm, TradeNo, InitialNow),
            CancellationToken.None);
        (await captured.Service.GetOutstandingInstructionsAsync(captured.OrderId, CancellationToken.None))
            .Value.ShouldBeNull();

        var failed = CreateHarness();
        await failed.InitiateAsync(PaymentMethod.Atm, PaymentInfoUrl);
        await failed.InitiateAsync(PaymentMethod.Barcode, PaymentInfoUrl);
        (await failed.Service.GetOutstandingInstructionsAsync(failed.OrderId, CancellationToken.None))
            .Value.ShouldBeNull();
    }

    [Fact(DisplayName = "S1 Captured 摘要仍保留付款方式與取號資訊")]
    public async Task S1_summary_retains_instructions_after_capture()
    {
        var harness = CreateHarness();
        var payment = await harness.IssueAsync(PaymentMethod.Atm);
        await harness.Service.HandleEcpayCallbackAsync(
            harness.BuildPayment(payment, PaymentMethod.Atm, TradeNo, InitialNow),
            CancellationToken.None);

        var summary = payment.ToSummary();
        summary.Method.ShouldBe(PaymentMethod.Atm);
        summary.Instructions.ShouldNotBeNull();
        summary.Instructions.VirtualAccount.ShouldBe("1234567890123456");
    }

    [Fact(DisplayName = "T1 信用卡付款結果早 21 分鐘仍回 stale-callback")]
    public async Task T1_credit_callback_age_boundary_is_unchanged()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync();

        var result = await harness.Service.HandleEcpayCallbackAsync(
            harness.BuildPayment(payment, PaymentMethod.CreditCard, TradeNo, InitialNow.AddMinutes(-21)),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("payment.stale-callback");
        payment.Status.ShouldBe(PaymentStatus.Pending);
    }

    private static Harness CreateHarness()
    {
        var repository = new StubRepository();
        var publisher = new RecordingPublisher();
        var unitOfWork = new RecordingUnitOfWork();
        var clock = new SettableClock(InitialNow);
        var settings = new EcpaySettings(
            MerchantId,
            new Uri("https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5"),
            new Uri("https://payment-stage.ecpay.com.tw/CreditDetail/DoAction"),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(20),
            false);
        var gateway = new EcpayGateway(settings, HashKey, HashIv, new HttpClient());
        var service = new PaymentApplicationService(
            repository,
            unitOfWork,
            publisher,
            gateway,
            settings,
            clock,
            new StubCorrelationContext());
        return new Harness(service, repository, unitOfWork, publisher, clock, OrderId.New());
    }

    private sealed class Harness(
        PaymentApplicationService service,
        StubRepository repository,
        RecordingUnitOfWork unitOfWork,
        RecordingPublisher publisher,
        SettableClock clock,
        OrderId orderId)
    {
        public PaymentApplicationService Service { get; } = service;
        public StubRepository Repository { get; } = repository;
        public RecordingUnitOfWork UnitOfWork { get; } = unitOfWork;
        public RecordingPublisher Publisher { get; } = publisher;
        public SettableClock Clock { get; } = clock;
        public OrderId OrderId { get; } = orderId;
        public PaymentInitiation? LastInitiation { get; private set; }

        public async Task<PaymentEntity> InitiateAsync(
            PaymentMethod method = PaymentMethod.CreditCard,
            Uri? paymentInfoUrl = null)
        {
            var result = await InitiateResultAsync(method, paymentInfoUrl);
            result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
            LastInitiation = result.Value;
            return Repository.Items.Single(payment =>
                payment.MerchantTradeNo == result.Value.Fields["MerchantTradeNo"]);
        }

        public Task<Result<PaymentInitiation>> InitiateResultAsync(
            PaymentMethod method,
            Uri? paymentInfoUrl) =>
            Service.InitiateAsync(
                new PaymentInitiationRequest(
                    OrderId,
                    Money.OfMajor(100, Currency.TWD),
                    Money.OfMajor(60, Currency.TWD),
                    "BE-62 payment",
                    new Uri("https://api.example.test/ecpay-result"),
                    new Uri("https://shop.example.test/payment-result"),
                    method,
                    paymentInfoUrl),
                CancellationToken.None);

        public async Task<PaymentEntity> IssueAsync(PaymentMethod method)
        {
            var payment = await InitiateAsync(method, PaymentInfoUrl);
            var result = await Service.HandleEcpayPaymentInfoAsync(
                BuildInfo(payment, method),
                CancellationToken.None);
            result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
            return payment;
        }

        public Dictionary<string, string> BuildInfo(
            PaymentEntity payment,
            PaymentMethod method,
            int? rtnCode = null,
            DateTimeOffset? tradeDate = null)
        {
            var fields = CommonFields(
                payment,
                rtnCode ?? (method == PaymentMethod.Atm ? 2 : 10100073),
                PaymentType(method),
                TradeNo,
                tradeDate ?? Clock.UtcNow);
            switch (method)
            {
                case PaymentMethod.Atm:
                    fields["BankCode"] = "812";
                    fields["vAccount"] = "1234567890123456";
                    fields["ExpireDate"] = "2026/10/04";
                    break;
                case PaymentMethod.ConvenienceStoreCode:
                    fields["PaymentNo"] = "CVS1234567890";
                    fields["ExpireDate"] = "2026/10/04 20:15:00";
                    break;
                case PaymentMethod.Barcode:
                    fields["Barcode1"] = "BARCODE-1";
                    fields["Barcode2"] = "BARCODE-2";
                    fields["Barcode3"] = "BARCODE-3";
                    fields["ExpireDate"] = "2026/10/04 20:15:00";
                    break;
            }

            Resign(fields);
            return fields;
        }

        public Dictionary<string, string> BuildPayment(
            PaymentEntity payment,
            PaymentMethod method,
            string tradeNo,
            DateTimeOffset paidAt)
        {
            var fields = CommonFields(payment, 1, PaymentType(method), tradeNo, Clock.UtcNow);
            fields["PaymentDate"] = Taipei(paidAt);
            fields["PaymentTypeChargeFee"] = "0";
            Resign(fields);
            return fields;
        }

        public void Resign(Dictionary<string, string> fields)
        {
            fields.Remove("CheckMacValue");
            fields["CheckMacValue"] = EcpayGateway.ComputeCheckMacValue(fields, HashKey, HashIv);
        }

        private static Dictionary<string, string> CommonFields(
            PaymentEntity payment,
            int rtnCode,
            string paymentType,
            string tradeNo,
            DateTimeOffset tradeDate) => new(StringComparer.Ordinal)
        {
            ["MerchantID"] = MerchantId,
            ["MerchantTradeNo"] = payment.MerchantTradeNo,
            ["TradeNo"] = tradeNo,
            ["RtnCode"] = rtnCode.ToString(CultureInfo.InvariantCulture),
            ["RtnMsg"] = rtnCode is 1 or 2 or 10100073 ? "成功" : "失敗",
            ["TradeAmt"] = "160",
            ["PaymentType"] = paymentType,
            ["TradeDate"] = Taipei(tradeDate),
            ["SimulatePaid"] = "0",
        };

        private static string PaymentType(PaymentMethod method) => method switch
        {
            PaymentMethod.CreditCard => "Credit_CreditCard",
            PaymentMethod.Atm => "ATM_BOT",
            PaymentMethod.ConvenienceStoreCode => "CVS_CVS",
            PaymentMethod.Barcode => "BARCODE_BARCODE",
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };

        private static string Taipei(DateTimeOffset instant) =>
            TimeZoneInfo.ConvertTime(instant, TaipeiTime.Zone)
                .ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private sealed class StubRepository : IPaymentRepository
    {
        public List<PaymentEntity> Items { get; } = [];
        public void Add(PaymentEntity payment) => Items.Add(payment);
        public Task<PaymentEntity?> FindByOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.LastOrDefault(payment => payment.OrderId == orderId));
        public Task<PaymentEntity?> FindByMerchantTradeNoAsync(TenantId tenantId, string merchantTradeNo, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(payment => payment.MerchantTradeNo == merchantTradeNo));
        public Task<PaymentEntity?> FindByIdAsync(TenantId tenantId, PaymentId id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(payment => payment.Id == id));
        public Task<IReadOnlyList<PaymentEntity>> FindByOrderAllAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PaymentEntity>>(Items.Where(payment => payment.OrderId == orderId).ToArray());
        public Task<PaymentEntity?> FindCapturedOrRefundedByOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(payment =>
                payment.OrderId == orderId && payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded));
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(++SaveCount);
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

    private sealed class SettableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public DateOnly TodayInTaipei => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, TaipeiTime.Zone).DateTime);
    }

    private sealed class StubCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "be620000000000000000000000000001";
        public string? CausationId => "be62000000000001";
        public TenantId TenantId => TenantId.Default;
    }
}
