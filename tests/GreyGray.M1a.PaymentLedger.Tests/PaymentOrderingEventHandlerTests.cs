using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Payment.Core;
using GreyGray.Modules.Payment.Infra;
using GreyGray.Platform.Abstractions.Audit;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Shouldly;
using Xunit;
using PaymentEntity = GreyGray.Modules.Payment.Core.Payment;

namespace GreyGray.M1a.PaymentLedger.Tests;

public sealed class PaymentOrderingEventHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "PaymentRequested 不得用只有總額的事件建立錯誤貨款運費拆分")]
    public async Task Payment_requested_does_not_invent_a_breakdown()
    {
        var repository = new StubPaymentRepository();
        var handler = new PaymentRequestedHandler();
        var requested = new PaymentRequested(
            Guid.CreateVersion7(),
            Now,
            TenantId.Default,
            OrderId.New(),
            GreyGray.Modules.Identity.Contracts.CustomerId.New(),
            Money.OfMajor(160, Currency.TWD),
            "checkout-key");

        await handler.HandleAsync(requested, CancellationToken.None);
        await handler.HandleAsync(requested, CancellationToken.None);

        repository.Items.ShouldBeEmpty();
    }

    [Fact(DisplayName = "儲值金退款更新付款狀態並只發一次 PaymentRefunded")]
    public async Task Stored_value_refund_publishes_once()
    {
        var payment = CapturedPayment();
        var repository = new StubPaymentRepository(payment);
        var publisher = new RecordingPublisher();
        var handler = new RefundRequestedHandler(
            repository, publisher, new StubGateway(), new StubClock(Now), auditWriter: null);
        var requested = new RefundRequested(
            Guid.CreateVersion7(),
            Now,
            payment.TenantId,
            payment.OrderId,
            null,
            payment.Amount,
            RefundDestination.StoredValue,
            "管理員取消");

        await handler.HandleAsync(requested, CancellationToken.None);
        await handler.HandleAsync(requested, CancellationToken.None);

        payment.Status.ShouldBe(PaymentStatus.Refunded);
        var refunded = publisher.Events.ShouldHaveSingleItem().ShouldBeOfType<PaymentRefunded>();
        refunded.RefundId.ShouldBe(new RefundId(requested.EventId));
        refunded.Destination.ShouldBe(RefundDestination.StoredValue);
        refunded.Amount.ShouldBe(payment.Amount);
    }

    [Fact(DisplayName = "單一品項退款先轉 PartiallyRefunded，累計到原付款總額才轉 Refunded")]
    public async Task Line_refunds_accumulate_until_payment_is_fully_refunded()
    {
        var payment = CapturedPayment();
        var publisher = new RecordingPublisher();
        var handler = new RefundRequestedHandler(
            new StubPaymentRepository(payment),
            publisher,
            new StubGateway(),
            new StubClock(Now),
            auditWriter: null);
        var firstLine = OrderLineId.New();
        var first = new RefundRequested(
            Guid.CreateVersion7(),
            Now,
            payment.TenantId,
            payment.OrderId,
            firstLine,
            Money.OfMajor(40, Currency.TWD),
            RefundDestination.StoredValue,
            "第一項缺貨");

        await handler.HandleAsync(first, CancellationToken.None);

        payment.Status.ShouldBe(PaymentStatus.PartiallyRefunded);
        payment.RefundedAmount.ShouldBe(Money.OfMajor(40, Currency.TWD));
        publisher.Events.Single().ShouldBeOfType<PaymentRefunded>().LineId.ShouldBe(firstLine);

        var second = new RefundRequested(
            Guid.CreateVersion7(),
            Now,
            payment.TenantId,
            payment.OrderId,
            OrderLineId.New(),
            Money.OfMajor(120, Currency.TWD),
            RefundDestination.StoredValue,
            "第二項缺貨");
        await handler.HandleAsync(second, CancellationToken.None);

        payment.Status.ShouldBe(PaymentStatus.Refunded);
        payment.RefundedAmount.ShouldBe(payment.Amount);
        publisher.Events.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "原路退款呼叫綠界成功後才轉已退款並發一次 PaymentRefunded")]
    public async Task Original_method_refund_succeeds_when_ecpay_accepts()
    {
        var payment = CapturedPayment();
        var publisher = new RecordingPublisher();
        var gateway = new StubGateway(new EcpayRefundResult(true, "1", "執行成功", "raw-success"));
        var auditWriter = new RecordingAuditWriter();
        var handler = new RefundRequestedHandler(
            new StubPaymentRepository(payment), publisher, gateway, new StubClock(Now), auditWriter);
        var requested = new RefundRequested(
            Guid.CreateVersion7(),
            Now,
            payment.TenantId,
            payment.OrderId,
            null,
            payment.Amount,
            RefundDestination.OriginalPaymentMethod,
            "管理員取消");

        await handler.HandleAsync(requested, CancellationToken.None);

        gateway.RefundCallCount.ShouldBe(1);
        payment.Status.ShouldBe(PaymentStatus.Refunded);
        var refunded = publisher.Events.ShouldHaveSingleItem().ShouldBeOfType<PaymentRefunded>();
        refunded.Destination.ShouldBe(RefundDestination.OriginalPaymentMethod);
        auditWriter.Entries.ShouldHaveSingleItem().Action.ShouldBe("payment.ecpay.refund.succeeded");
    }

    [Fact(DisplayName = "綠界拒絕退款時是業務失敗：不改狀態、不發事件、不 throw，但要留痕")]
    public async Task Original_method_refund_stays_captured_when_ecpay_rejects()
    {
        var payment = CapturedPayment();
        var publisher = new RecordingPublisher();
        var gateway = new StubGateway(new EcpayRefundResult(false, "10100248", "帳戶餘額不足", "raw-fail"));
        var auditWriter = new RecordingAuditWriter();
        var handler = new RefundRequestedHandler(
            new StubPaymentRepository(payment), publisher, gateway, new StubClock(Now), auditWriter);
        var requested = new RefundRequested(
            Guid.CreateVersion7(),
            Now,
            payment.TenantId,
            payment.OrderId,
            null,
            payment.Amount,
            RefundDestination.OriginalPaymentMethod,
            "管理員取消");

        await handler.HandleAsync(requested, CancellationToken.None);

        gateway.RefundCallCount.ShouldBe(1);
        payment.Status.ShouldBe(PaymentStatus.Captured);
        publisher.Events.ShouldBeEmpty();
        var entry = auditWriter.Entries.ShouldHaveSingleItem();
        entry.Action.ShouldBe("payment.ecpay.refund.failed");
        entry.PayloadJson.ShouldContain("帳戶餘額不足");
    }

    [Fact(DisplayName = "同一筆原路退款重送不可重複呼叫綠界：本地累計上限先擋下")]
    public async Task Original_method_refund_does_not_call_ecpay_twice_for_the_same_event()
    {
        var payment = CapturedPayment();
        var publisher = new RecordingPublisher();
        var gateway = new StubGateway(new EcpayRefundResult(true, "1", "執行成功", "raw-success"));
        var handler = new RefundRequestedHandler(
            new StubPaymentRepository(payment), publisher, gateway, new StubClock(Now), auditWriter: null);
        var requested = new RefundRequested(
            Guid.CreateVersion7(),
            Now,
            payment.TenantId,
            payment.OrderId,
            null,
            payment.Amount,
            RefundDestination.OriginalPaymentMethod,
            "管理員取消");

        await handler.HandleAsync(requested, CancellationToken.None);
        await handler.HandleAsync(requested, CancellationToken.None);

        gateway.RefundCallCount.ShouldBe(1);
        payment.Status.ShouldBe(PaymentStatus.Refunded);
        publisher.Events.ShouldHaveSingleItem();
    }

    [Fact(DisplayName = "超過原付款可退餘額時，本地先擋下，完全不呼叫綠界")]
    public async Task Original_method_refund_rejects_amount_exceeding_local_cap_without_calling_ecpay()
    {
        var payment = CapturedPayment();
        var publisher = new RecordingPublisher();
        var gateway = new StubGateway(new EcpayRefundResult(true, "1", "執行成功", "raw-success"));
        var handler = new RefundRequestedHandler(
            new StubPaymentRepository(payment), publisher, gateway, new StubClock(Now), auditWriter: null);
        var requested = new RefundRequested(
            Guid.CreateVersion7(),
            Now,
            payment.TenantId,
            payment.OrderId,
            null,
            payment.Amount.Add(Money.OfMajor(1, Currency.TWD)),
            RefundDestination.OriginalPaymentMethod,
            "管理員取消");

        await handler.HandleAsync(requested, CancellationToken.None);

        gateway.RefundCallCount.ShouldBe(0);
        payment.Status.ShouldBe(PaymentStatus.Captured);
        publisher.Events.ShouldBeEmpty();
    }

    private static PaymentEntity CapturedPayment()
    {
        var payment = PaymentEntity.Start(
            PaymentId.New(),
            TenantId.Default,
            OrderId.New(),
            Money.OfMajor(100, Currency.TWD),
            Money.OfMajor(60, Currency.TWD),
            "GG202608280000000001",
            Now.AddMinutes(-5),
            Now.AddMinutes(25));
        payment.Capture("2608280000000001", Money.OfMajor(3, Currency.TWD), Now);
        return payment;
    }

    private sealed class StubClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;

        public DateOnly TodayInTaipei => DateOnly.FromDateTime(now.UtcDateTime.AddHours(8));
    }

    private sealed class StubGateway(EcpayRefundResult? refundResult = null) : IEcpayGateway
    {
        public int RefundCallCount { get; private set; }

        public IReadOnlyDictionary<string, string> CreateCheckoutFields(
            string merchantTradeNo,
            Money amount,
            string description,
            Uri returnUrl,
            Uri clientBackUrl,
            DateTimeOffset createdAt,
            PaymentMethod method,
            Uri? paymentInfoUrl) => new Dictionary<string, string>();

        public bool VerifyCallback(IReadOnlyDictionary<string, string> fields) => true;

        public Task<EcpayRefundResult> RequestRefundAsync(
            string merchantTradeNo,
            string providerTransactionId,
            Money amount,
            CancellationToken cancellationToken)
        {
            RefundCallCount++;
            return Task.FromResult(refundResult ?? new EcpayRefundResult(true, "1", "執行成功", "raw"));
        }
    }

    private sealed record AuditEntry(AuditCategory Category, string Action, string PayloadJson);

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(
            AuditCategory category,
            string action,
            string targetType,
            string targetRef,
            Guid? actorId,
            Guid? subjectId,
            string? reason,
            string payloadJson,
            CancellationToken cancellationToken)
        {
            Entries.Add(new AuditEntry(category, action, payloadJson));
            return Task.CompletedTask;
        }
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

    private sealed class StubPaymentRepository(params PaymentEntity[] payments) : IPaymentRepository
    {
        public List<PaymentEntity> Items { get; } = [.. payments];

        public void Add(PaymentEntity payment) => Items.Add(payment);

        public Task<PaymentEntity?> FindByOrderAsync(
            TenantId tenantId,
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(payment =>
                payment.TenantId == tenantId && payment.OrderId == orderId));

        public Task<PaymentEntity?> FindByMerchantTradeNoAsync(
            TenantId tenantId,
            string merchantTradeNo,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(payment =>
                payment.TenantId == tenantId && payment.MerchantTradeNo == merchantTradeNo));

        public Task<PaymentEntity?> FindByIdAsync(
            TenantId tenantId,
            PaymentId id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(payment =>
                payment.TenantId == tenantId && payment.Id == id));

        public Task<IReadOnlyList<PaymentEntity>> FindByOrderAllAsync(
            TenantId tenantId,
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PaymentEntity>>(Items
                .Where(payment => payment.TenantId == tenantId && payment.OrderId == orderId)
                .ToArray());

        public Task<PaymentEntity?> FindCapturedOrRefundedByOrderAsync(
            TenantId tenantId,
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(payment =>
                payment.TenantId == tenantId &&
                payment.OrderId == orderId &&
                payment.Status is PaymentStatus.Captured
                    or PaymentStatus.PartiallyRefunded
                    or PaymentStatus.Refunded));
    }
}
