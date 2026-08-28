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
        var handler = new RefundRequestedHandler(repository, publisher, new StubClock(Now));
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

    [Fact(DisplayName = "沒有 provider API 時原路退款明確失敗且不可改成已退款")]
    public async Task Original_method_refund_fails_closed()
    {
        var payment = CapturedPayment();
        var publisher = new RecordingPublisher();
        var handler = new RefundRequestedHandler(
            new StubPaymentRepository(payment),
            publisher,
            new StubClock(Now));
        var requested = new RefundRequested(
            Guid.CreateVersion7(),
            Now,
            payment.TenantId,
            payment.OrderId,
            null,
            payment.Amount,
            RefundDestination.OriginalPaymentMethod,
            "管理員取消");

        var exception = await Should.ThrowAsync<NotSupportedException>(() =>
            handler.HandleAsync(requested, CancellationToken.None));

        exception.Message.ShouldContain("不得把退款要求標成成功");
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
                payment.Status is PaymentStatus.Captured or PaymentStatus.Refunded));
    }
}
