using System.Globalization;
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

/// <summary>
/// BE-59 建立的非即時付款 characterization；BE-62 依 ADR-044 翻轉取號與晚到通知行為。
/// </summary>
public sealed class EcpayDeferredPaymentCharacterizationTests
{
    private const string MerchantId = "BE59MERCHANT";
    private const string HashKey = "BE59HASHKEY0001";
    private const string HashIv = "BE59HASHIV00001";
    private const string AtmTradeNo = "2609301200000001";
    private const string CvsTradeNo = "2609301200000002";

    private static readonly DateTimeOffset InitialNow =
        new(2026, 9, 30, 4, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "C0 現況正向對照：Pending 收到 RtnCode=1 會 Captured 並發 PaymentCaptured")]
    public async Task C0_success_notification_captures_pending_payment()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync();
        var notification = harness.BuildNotification(payment, 1, "Credit_CreditCard", AtmTradeNo);

        harness.AssertValidProductionCallback(notification);
        var result = await harness.Service.HandleEcpayCallbackAsync(
            notification,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        payment.Status.ShouldBe(PaymentStatus.Captured);
        payment.ProviderTransactionId.ShouldBe(AtmTradeNo);
        harness.Publisher.Events.ShouldHaveSingleItem().ShouldBeOfType<PaymentCaptured>();
    }

    [Fact(DisplayName = "C1 ATM 取號走 PaymentInfo handler，轉 InstructionsIssued 並發事件")]
    public async Task C1_atm_code_retrieval_issues_payment_instructions()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync(
            PaymentMethod.Atm,
            new Uri("https://api.example.test/v1/webhooks/ecpay/payment-info"));
        var notification = harness.BuildNotification(
            payment,
            2,
            "ATM_TAISHIN",
            AtmTradeNo,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BankCode"] = "812",
                ["vAccount"] = "1234567890123456",
                ["ExpireDate"] = "2026/10/04",
            },
            callbackInstant: InitialNow.AddHours(-2),
            includePaymentDate: false);

        var result = await harness.Service.HandleEcpayPaymentInfoAsync(
            notification,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        payment.Status.ShouldBe(PaymentStatus.InstructionsIssued);
        payment.ProviderTransactionId.ShouldBe(AtmTradeNo);
        payment.ProviderExpiresAt.ShouldBe(new DateTimeOffset(2026, 10, 4, 15, 59, 59, TimeSpan.Zero));
        harness.Publisher.Events.ShouldHaveSingleItem().ShouldBeOfType<PaymentInstructionsIssued>();
    }

    [Fact(DisplayName = "C2 CVS 取號走 PaymentInfo handler，轉 InstructionsIssued 並發事件")]
    public async Task C2_cvs_code_retrieval_issues_payment_instructions()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync(
            PaymentMethod.ConvenienceStoreCode,
            new Uri("https://api.example.test/v1/webhooks/ecpay/payment-info"));
        var notification = harness.BuildNotification(
            payment,
            10100073,
            "CVS_CVS",
            CvsTradeNo,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PaymentNo"] = "LLL12345678901",
                ["ExpireDate"] = "2026/10/07 12:00:00",
                ["Barcode1"] = string.Empty,
                ["Barcode2"] = string.Empty,
                ["Barcode3"] = string.Empty,
            },
            includePaymentDate: false);

        var result = await harness.Service.HandleEcpayPaymentInfoAsync(
            notification,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        payment.Status.ShouldBe(PaymentStatus.InstructionsIssued);
        payment.ProviderTransactionId.ShouldBe(CvsTradeNo);
        harness.Publisher.Events.ShouldHaveSingleItem().ShouldBeOfType<PaymentInstructionsIssued>();
    }

    [Fact(DisplayName = "C3 取號後同 TradeNo 付款完成會 Captured")]
    public async Task C3_paid_notification_after_code_retrieval_captures_payment()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync(
            PaymentMethod.Atm,
            new Uri("https://api.example.test/v1/webhooks/ecpay/payment-info"));
        var retrieval = harness.BuildNotification(
            payment,
            2,
            "ATM_TAISHIN",
            AtmTradeNo,
            new Dictionary<string, string> { ["BankCode"] = "812", ["vAccount"] = "1234567890123456", ["ExpireDate"] = "2026/10/04" },
            includePaymentDate: false);
        (await harness.Service.HandleEcpayPaymentInfoAsync(
            retrieval,
            TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        var paid = harness.BuildNotification(payment, 1, "ATM_TAISHIN", AtmTradeNo);
        var result = await harness.Service.HandleEcpayCallbackAsync(
            paid,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        payment.Status.ShouldBe(PaymentStatus.Captured);
        harness.Publisher.Events.Count.ShouldBe(2);
        harness.Publisher.Events[1].ShouldBeOfType<PaymentCaptured>();
    }

    [Fact(DisplayName = "C4 現況：31 分鐘後重新付款會 Expire 舊筆、不發事件並開新單號")]
    public async Task C4_reinitiation_after_31_minutes_expires_old_payment_without_event()
    {
        var harness = CreateHarness();
        var oldPayment = await harness.InitiateAsync();

        harness.Clock.UtcNow = InitialNow.AddMinutes(31);
        var newPayment = await harness.InitiateAsync();

        harness.Repository.Items.Count.ShouldBe(2);
        oldPayment.Status.ShouldBe(PaymentStatus.Failed);
        oldPayment.ProviderTransactionId.ShouldBeNull();
        newPayment.Status.ShouldBe(PaymentStatus.Pending);
        newPayment.MerchantTradeNo.ShouldNotBe(oldPayment.MerchantTradeNo);
        harness.Publisher.Events.ShouldBeEmpty();
    }

    [Fact(DisplayName = "C5 舊筆 Expire 後收到付款完成，寫晚到紀錄且不發收款事件")]
    public async Task C5_paid_notification_for_expired_attempt_records_late_capture()
    {
        var harness = CreateHarness();
        var oldPayment = await harness.InitiateAsync();
        harness.Clock.UtcNow = InitialNow.AddMinutes(31);
        await harness.InitiateAsync();

        var paid = harness.BuildNotification(oldPayment, 1, "Credit_CreditCard", AtmTradeNo);
        var result = await harness.Service.HandleEcpayCallbackAsync(
            paid,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        oldPayment.Status.ShouldBe(PaymentStatus.Failed);
        oldPayment.LateCaptureTradeNo.ShouldBe(AtmTradeNo);
        oldPayment.LateCapturedAt.ShouldNotBeNull();
        harness.Publisher.Events.ShouldBeEmpty();
    }

    [Theory(DisplayName = "C6 現況：PaymentDate 早 19 分可收款，早 21 分回 stale-callback")]
    [InlineData(19, true)]
    [InlineData(21, false)]
    public async Task C6_callback_age_has_a_20_minute_boundary(
        int minutesOld,
        bool shouldCapture)
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync();
        var notification = harness.BuildNotification(
            payment,
            1,
            "Credit_CreditCard",
            AtmTradeNo,
            callbackInstant: harness.Clock.UtcNow.AddMinutes(-minutesOld));

        harness.AssertValidProductionCallback(notification);
        var result = await harness.Service.HandleEcpayCallbackAsync(
            notification,
            TestContext.Current.CancellationToken);

        if (shouldCapture)
        {
            result.IsSuccess.ShouldBeTrue();
            payment.Status.ShouldBe(PaymentStatus.Captured);
            harness.Publisher.Events.ShouldHaveSingleItem().ShouldBeOfType<PaymentCaptured>();
        }
        else
        {
            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe("payment.stale-callback");
            payment.Status.ShouldBe(PaymentStatus.Pending);
            harness.Publisher.Events.ShouldBeEmpty();
        }
    }

    [Fact(DisplayName = "C7 舊筆 Expire 後收到失敗通知，回業務失敗而不丟例外")]
    public async Task C7_failure_notification_for_expired_attempt_returns_business_failure()
    {
        var harness = CreateHarness();
        var oldPayment = await harness.InitiateAsync();
        harness.Clock.UtcNow = InitialNow.AddMinutes(31);
        await harness.InitiateAsync();

        var failed = harness.BuildNotification(oldPayment, 7, "Credit_CreditCard", AtmTradeNo);
        var result = await harness.Service.HandleEcpayCallbackAsync(
            failed,
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("payment.provider-trade-no-mismatch");
        oldPayment.Status.ShouldBe(PaymentStatus.Failed);
        harness.Publisher.Events.ShouldBeEmpty();
    }

    [Fact(DisplayName = "C8 現況：Pending 已過 ExpiresAt 三天但未重啟，時間窗內通知仍會收款")]
    public async Task C8_expired_pending_payment_is_still_captured_without_reinitiation()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync();
        harness.Clock.UtcNow = InitialNow.AddDays(3);
        var paid = harness.BuildNotification(payment, 1, "Credit_CreditCard", AtmTradeNo);

        harness.AssertValidProductionCallback(paid);
        var result = await harness.Service.HandleEcpayCallbackAsync(
            paid,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        payment.Status.ShouldBe(PaymentStatus.Captured);
        harness.Publisher.Events.ShouldHaveSingleItem().ShouldBeOfType<PaymentCaptured>();
    }

    [Fact(DisplayName = "C9 現況：SimulatePaid=1 在旗標關閉時被拒絕且維持 Pending")]
    public async Task C9_simulated_paid_notification_is_rejected()
    {
        var harness = CreateHarness();
        var payment = await harness.InitiateAsync();
        var paid = harness.BuildNotification(
            payment,
            1,
            "Credit_CreditCard",
            AtmTradeNo,
            simulatePaid: "1");

        harness.Gateway.VerifyCallback(paid).ShouldBeTrue();
        var result = await harness.Service.HandleEcpayCallbackAsync(
            paid,
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("payment.simulated-callback-rejected");
        payment.Status.ShouldBe(PaymentStatus.Pending);
        harness.Publisher.Events.ShouldBeEmpty();
    }

    [Fact(DisplayName = "C10 現況：10 分鐘後重新付款沿用同一筆 Payment 與 MerchantTradeNo")]
    public async Task C10_reinitiation_after_10_minutes_reuses_the_pending_attempt()
    {
        var harness = CreateHarness();
        var first = await harness.InitiateAsync();
        var firstTradeNo = first.MerchantTradeNo;

        harness.Clock.UtcNow = InitialNow.AddMinutes(10);
        var second = await harness.InitiateAsync();

        harness.Repository.Items.ShouldHaveSingleItem().ShouldBeSameAs(first);
        second.ShouldBeSameAs(first);
        second.MerchantTradeNo.ShouldBe(firstTradeNo);
        second.Status.ShouldBe(PaymentStatus.Pending);
        harness.Publisher.Events.ShouldBeEmpty();
    }

    private static Harness CreateHarness()
    {
        var repository = new StubRepository();
        var publisher = new RecordingPublisher();
        var clock = new SettableClock(InitialNow);
        var settings = new EcpaySettings(
            MerchantId,
            new Uri("https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5"),
            new Uri("https://payment-stage.ecpay.com.tw/CreditDetail/DoAction"),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(20),
            AllowSimulatedPaid: false);
        var gateway = new EcpayGateway(settings, HashKey, HashIv, new HttpClient());
        var service = new PaymentApplicationService(
            repository,
            new NoopUnitOfWork(),
            publisher,
            gateway,
            settings,
            clock,
            new StubCorrelationContext());
        return new Harness(
            service,
            repository,
            publisher,
            gateway,
            clock,
            OrderId.New());
    }

    private sealed class Harness(
        PaymentApplicationService service,
        StubRepository repository,
        RecordingPublisher publisher,
        EcpayGateway gateway,
        SettableClock clock,
        OrderId orderId)
    {
        public PaymentApplicationService Service { get; } = service;

        public StubRepository Repository { get; } = repository;

        public RecordingPublisher Publisher { get; } = publisher;

        public EcpayGateway Gateway { get; } = gateway;

        public SettableClock Clock { get; } = clock;

        public async Task<PaymentEntity> InitiateAsync(
            PaymentMethod method = PaymentMethod.CreditCard,
            Uri? paymentInfoUrl = null)
        {
            var result = await Service.InitiateAsync(
                new PaymentInitiationRequest(
                    orderId,
                    Money.OfMajor(100, Currency.TWD),
                    Money.OfMajor(60, Currency.TWD),
                    "GreyGray BE-59 characterization",
                    new Uri("https://api.example.test/v1/webhooks/ecpay"),
                    new Uri($"https://shop.example.test/payment/result?orderId={orderId.Value:N}"),
                    method,
                    paymentInfoUrl),
                TestContext.Current.CancellationToken);

            result.IsSuccess.ShouldBeTrue();
            var merchantTradeNo = result.Value.Fields["MerchantTradeNo"];
            return Repository.Items.Single(payment =>
                StringComparer.Ordinal.Equals(payment.MerchantTradeNo, merchantTradeNo));
        }

        public Dictionary<string, string> BuildNotification(
            PaymentEntity payment,
            int rtnCode,
            string paymentType,
            string tradeNo,
            IReadOnlyDictionary<string, string>? additionalFields = null,
            DateTimeOffset? callbackInstant = null,
            string simulatePaid = "0",
            bool includePaymentDate = true)
        {
            var callbackTime = ToTaipeiWallTime(callbackInstant ?? Clock.UtcNow);
            var tradeTime = ToTaipeiWallTime(Clock.UtcNow);
            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["MerchantID"] = MerchantId,
                ["MerchantTradeNo"] = payment.MerchantTradeNo,
                ["TradeNo"] = tradeNo,
                ["RtnCode"] = rtnCode.ToString(CultureInfo.InvariantCulture),
                ["RtnMsg"] = rtnCode == 1 ? "交易成功" : "取號成功",
                ["TradeAmt"] = "160",
                ["PaymentType"] = paymentType,
                ["TradeDate"] = tradeTime,
                ["PaymentTypeChargeFee"] = "0",
                ["SimulatePaid"] = simulatePaid,
            };
            if (includePaymentDate)
            {
                fields["PaymentDate"] = callbackTime;
            }
            if (additionalFields is not null)
            {
                foreach (var pair in additionalFields)
                {
                    fields[pair.Key] = pair.Value;
                }
            }

            fields["CheckMacValue"] = EcpayGateway.ComputeCheckMacValue(fields, HashKey, HashIv);
            return fields;
        }

        public void AssertValidProductionCallback(IReadOnlyDictionary<string, string> fields)
        {
            Gateway.VerifyCallback(fields).ShouldBeTrue();
            fields["MerchantID"].ShouldBe(MerchantId);
            fields["MerchantTradeNo"].ShouldNotBeNullOrWhiteSpace();
            fields["TradeNo"].ShouldNotBeNullOrWhiteSpace();
            fields["TradeAmt"].ShouldBe("160");
            fields["SimulatePaid"].ShouldBe("0");
            fields["PaymentDate"].ShouldNotBeNullOrWhiteSpace();
            fields["TradeDate"].ShouldNotBeNullOrWhiteSpace();
        }

        private static string ToTaipeiWallTime(DateTimeOffset instant) =>
            TimeZoneInfo.ConvertTime(instant, TaipeiTime.Zone)
                .ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
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

    private sealed class SettableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public DateOnly TodayInTaipei =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, TaipeiTime.Zone).DateTime);
    }

    private sealed class StubCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "00000000000000000000000000000059";

        public string? CausationId => "0000000000000059";

        public TenantId TenantId => GreyGray.Shared.Kernel.TenantId.Default;
    }
}
