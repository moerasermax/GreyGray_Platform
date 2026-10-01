using System.Reflection;
using System.Text.Json;
using AdminEndpoints = GreyGray.Api.Admin.M1aEndpoints;
using StorefrontEndpoints = GreyGray.Api.Storefront.M1aEndpoints;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;
using GreyGray.Shared.Kernel.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

public sealed class OrderDeadlineHostTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 4, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "H1：待付款期限到期當秒仍有效，下一秒才逾期；空期限與非待付款不逾期")]
    public async Task Payment_overdue_uses_the_last_valid_second_boundary()
    {
        var cases = new[]
        {
            (OrderStatus.AwaitingPayment, (DateTimeOffset?)Now, Now, false, 1),
            (OrderStatus.AwaitingPayment, (DateTimeOffset?)Now, Now.AddSeconds(1), true, 0),
            (OrderStatus.AwaitingPayment, (DateTimeOffset?)null, Now.AddYears(1), false, 1),
            (OrderStatus.PaidAwaitingClose, (DateTimeOffset?)Now.AddDays(-1), Now, false, 0),
            (OrderStatus.Cancelled, (DateTimeOffset?)Now.AddDays(-1), Now, false, 0),
        };

        foreach (var (status, dueAt, clockNow, expectedOverdue, expectedQueries) in cases)
        {
            var order = Order(CustomerId.New(), status, dueAt);
            var query = new RecordingInstructionsQuery(Instructions());

            using var document = await GetOrderAsync(order, query, new FakeClock(clockNow));

            document.RootElement.GetProperty("paymentOverdue").GetBoolean().ShouldBe(expectedOverdue);
            query.Calls.ShouldBe(expectedQueries);
        }
    }

    [Fact(DisplayName = "H2：逾期訂單不查取號資訊且固定回 null")]
    public async Task Overdue_order_skips_payment_instructions_query()
    {
        var order = Order(CustomerId.New(), OrderStatus.AwaitingPayment, Now.AddSeconds(-1));
        var query = new RecordingInstructionsQuery(Instructions());

        using var document = await GetOrderAsync(order, query, new FakeClock(Now));

        document.RootElement.GetProperty("paymentOverdue").GetBoolean().ShouldBeTrue();
        document.RootElement.GetProperty("paymentInstructions").ValueKind.ShouldBe(JsonValueKind.Null);
        query.Calls.ShouldBe(0);
    }

    [Theory(DisplayName = "H3／H4：已取消訂單照原樣序列化三種取消來源")]
    [InlineData(OrderCancellationSource.Customer, "Customer")]
    [InlineData(OrderCancellationSource.Staff, "Staff")]
    [InlineData(OrderCancellationSource.PaymentExpired, "PaymentExpired")]
    public async Task Cancelled_order_serializes_each_cancellation_source(
        OrderCancellationSource source,
        string expected)
    {
        var cancelledAt = Now.AddMinutes(-5);
        var order = Order(CustomerId.New(), OrderStatus.Cancelled, Now.AddDays(-1)) with
        {
            CancelledAt = cancelledAt,
            CancellationSource = source,
        };

        using var document = await GetOrderAsync(
            order,
            new RecordingInstructionsQuery(null),
            new FakeClock(Now));

        document.RootElement.GetProperty("cancelledAt").GetDateTimeOffset().ShouldBe(cancelledAt);
        document.RootElement.GetProperty("cancellationSource").GetString().ShouldBe(expected);
        document.RootElement.GetProperty("paymentOverdue").GetBoolean().ShouldBeFalse();
    }

    [Fact(DisplayName = "H3：未取消與舊取消資料保留各自的 null 語意")]
    public async Task Cancellation_fields_preserve_new_and_legacy_null_shapes()
    {
        var active = Order(CustomerId.New(), OrderStatus.AwaitingPayment, Now.AddDays(1));
        using var activeDocument = await GetOrderAsync(
            active,
            new RecordingInstructionsQuery(null),
            new FakeClock(Now));
        activeDocument.RootElement.GetProperty("cancelledAt").ValueKind.ShouldBe(JsonValueKind.Null);
        activeDocument.RootElement.GetProperty("cancellationSource").ValueKind.ShouldBe(JsonValueKind.Null);

        var cancelledAt = Now.AddDays(-10);
        var legacy = Order(CustomerId.New(), OrderStatus.Cancelled, Now.AddDays(-11)) with
        {
            CancelledAt = cancelledAt,
            CancellationSource = null,
        };
        using var legacyDocument = await GetOrderAsync(
            legacy,
            new RecordingInstructionsQuery(null),
            new FakeClock(Now));
        legacyDocument.RootElement.GetProperty("cancelledAt").GetDateTimeOffset().ShouldBe(cancelledAt);
        legacyDocument.RootElement.GetProperty("cancellationSource").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact(DisplayName = "H3：寬限中的待付款訂單仍可自助取消並回 Customer 來源")]
    public async Task Overdue_order_can_still_be_cancelled_by_customer()
    {
        var customer = CustomerId.New();
        var overdue = Order(customer, OrderStatus.AwaitingPayment, Now.AddSeconds(-1));
        var cancelled = overdue with
        {
            Status = OrderStatus.Cancelled,
            CancelledAt = Now,
            CancellationSource = OrderCancellationSource.Customer,
        };
        var ordering = new StubOrderingApplication(overdue) { CancelledResult = cancelled };
        var sessions = new StorefrontCheckoutEndpointTests.FakeSessionStore();
        var context = Context(sessions.Issue(customer), "cancel-overdue");

        var result = await StorefrontEndpoints.CancelCustomerOrderAsync(
            overdue.Id.ToString(),
            new StorefrontEndpoints.CancelOrderInput("客人取消"),
            context,
            sessions,
            ordering,
            Catalog(),
            CustomerDirectory(customer),
            new InspectableIdempotencyStore(),
            NullLogger.Instance,
            TestContext.Current.CancellationToken);
        using var document = await ExecuteAsync(result, context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        ordering.CancelCustomerCalls.ShouldBe(1);
        document.RootElement.GetProperty("paymentOverdue").GetBoolean().ShouldBeFalse();
        document.RootElement.GetProperty("cancelledAt").GetDateTimeOffset().ShouldBe(Now);
        document.RootElement.GetProperty("cancellationSource").GetString().ShouldBe("Customer");
    }

    [Fact(DisplayName = "H3／H4：結帳共用 render 固定不計逾期並保留取消欄位形狀")]
    public async Task Shared_checkout_render_does_not_compute_payment_overdue()
    {
        var order = Order(CustomerId.New(), OrderStatus.AwaitingPayment, Now.AddDays(-1));

        using var document = await RenderOrderAsync(order);

        document.RootElement.GetProperty("paymentOverdue").GetBoolean().ShouldBeFalse();
        document.RootElement.GetProperty("cancelledAt").ValueKind.ShouldBe(JsonValueKind.Null);
        document.RootElement.GetProperty("cancellationSource").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact(DisplayName = "H5：逾期付款回 422、abandon，重送同 key 仍不呼叫 Payment")]
    public async Task Overdue_payment_is_rejected_and_same_key_can_retry_safely()
    {
        var customer = CustomerId.New();
        var order = Order(customer, OrderStatus.AwaitingPayment, Now.AddSeconds(-1));
        var ordering = new StubOrderingApplication(order);
        var payments = new RecordingPaymentCommand();
        var idempotency = new InspectableIdempotencyStore();
        var sessions = new StorefrontCheckoutEndpointTests.FakeSessionStore();
        var token = sessions.Issue(customer);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var context = Context(token, "overdue-key");
            var result = await StorefrontEndpoints.InitiateCustomerPaymentAsync(
                order.Id.ToString(), context, sessions, ordering, payments, idempotency,
                Configuration(), new FakeClock(Now), TestContext.Current.CancellationToken);
            using var document = await ExecuteAsync(result, context);

            context.Response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
            document.RootElement.GetProperty("code").GetString().ShouldBe("ordering.payment-overdue");
            document.RootElement.GetProperty("title").GetString().ShouldBe(
                "繳費期限已過，正在等待確認付款，不能再付款。");
            idempotency.StatusOf("overdue-key", "storefront:orders:payment").ShouldBe(
                InspectableIdempotencyStore.EntryStatus.Abandoned);
        }

        payments.InitiateCalls.ShouldBe(0);
    }

    [Fact(DisplayName = "H5：已取消優先於逾期，仍回既有 409")]
    public async Task Cancelled_payment_is_rejected_before_overdue_check()
    {
        var customer = CustomerId.New();
        var order = Order(customer, OrderStatus.Cancelled, Now.AddDays(-1));
        var payments = new RecordingPaymentCommand();

        var (context, document) = await InitiateAsync(order, payments, "cancelled-key");
        using (document)
        {
            context.Response.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
            document.RootElement.GetProperty("code").GetString().ShouldBe("ordering.order-cancelled");
        }
        payments.InitiateCalls.ShouldBe(0);
    }

    [Theory(DisplayName = "H5：已付款狀態即使期限已過也照舊呼叫 Payment")]
    [InlineData(OrderStatus.PaidAwaitingClose)]
    [InlineData(OrderStatus.ReadyToShip)]
    public async Task Paid_order_with_past_due_date_is_not_overdue(OrderStatus status)
    {
        var order = Order(CustomerId.New(), status, Now.AddDays(-1));
        var payments = new RecordingPaymentCommand();

        var (context, document) = await InitiateAsync(order, payments, $"paid-{status}");
        using (document)
        {
            context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        }
        payments.InitiateCalls.ShouldBe(1);
    }

    [Fact(DisplayName = "H5：期限尚有效時照舊呼叫 Payment 一次")]
    public async Task Payment_before_deadline_reaches_payment_module()
    {
        var order = Order(CustomerId.New(), OrderStatus.AwaitingPayment, Now);
        var payments = new RecordingPaymentCommand();

        var (context, document) = await InitiateAsync(order, payments, "before-deadline");
        using (document)
        {
            context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        }
        payments.InitiateCalls.ShouldBe(1);
        payments.LastRequest.ShouldNotBeNull();
        payments.LastRequest!.OrderId.ShouldBe(order.Id);
    }

    [Fact(DisplayName = "H6／H4：後台回應帶 paymentDueAt 與 cancellationSource JSON")]
    public async Task Admin_order_projects_deadline_and_cancellation_source()
    {
        var customer = CustomerId.New();
        var order = Order(customer, OrderStatus.Cancelled, Now) with
        {
            CancelledAt = Now.AddMinutes(1),
            CancellationSource = OrderCancellationSource.PaymentExpired,
        };

        var response = await AdminEndpoints.ToAdminOrderAsync(
            order,
            CustomerDirectory(customer),
            new FakePaymentQuery(),
            Catalog(),
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        response.PaymentDueAt.ShouldBe(Now);
        response.CancellationSource.ShouldBe(OrderCancellationSource.PaymentExpired);
        var json = JsonSerializer.Serialize(response, GreyGrayJson.Options);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("paymentDueAt").GetDateTimeOffset().ShouldBe(Now);
        document.RootElement.GetProperty("cancellationSource").GetString().ShouldBe("PaymentExpired");
    }

    private static async Task<(DefaultHttpContext Context, JsonDocument Document)> InitiateAsync(
        OrderView order,
        RecordingPaymentCommand payments,
        string key)
    {
        var sessions = new StorefrontCheckoutEndpointTests.FakeSessionStore();
        var context = Context(sessions.Issue(order.CustomerId), key);
        var result = await StorefrontEndpoints.InitiateCustomerPaymentAsync(
            order.Id.ToString(),
            context,
            sessions,
            new StubOrderingApplication(order),
            payments,
            new InspectableIdempotencyStore(),
            Configuration(),
            new FakeClock(Now),
            TestContext.Current.CancellationToken);
        return (context, await ExecuteAsync(result, context));
    }

    private static async Task<JsonDocument> GetOrderAsync(
        OrderView order,
        RecordingInstructionsQuery query,
        FakeClock clock)
    {
        var sessions = new StorefrontCheckoutEndpointTests.FakeSessionStore();
        var context = Context(sessions.Issue(order.CustomerId));
        var result = await StorefrontEndpoints.GetCustomerOrderAsync(
            order.Id.ToString(),
            context,
            sessions,
            new StubOrderingApplication(order),
            Catalog(),
            CustomerDirectory(order.CustomerId),
            query,
            clock,
            NullLogger.Instance,
            TestContext.Current.CancellationToken);
        return await ExecuteAsync(result, context);
    }

    private static async Task<JsonDocument> RenderOrderAsync(OrderView order)
    {
        var method = typeof(StorefrontEndpoints).GetMethod(
            "ToOrderAsync",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(
            null,
            [order, Catalog(), CustomerDirectory(order.CustomerId), NullLogger.Instance,
                TestContext.Current.CancellationToken])!;
        await task;
        var response = task.GetType().GetProperty("Result")!.GetValue(task)!;
        return JsonDocument.Parse(JsonSerializer.Serialize(response, response.GetType(), GreyGrayJson.Options));
    }

    private static async Task<JsonDocument> ExecuteAsync(IResult result, DefaultHttpContext context)
    {
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(
            context.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static DefaultHttpContext Context(string sessionToken, string? key = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpJsonOptions(options =>
            BffHttp.ApplyGreyGrayJson(options.SerializerOptions));
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.greygray.test");
        context.Request.Headers.Cookie = $"gg_session={sessionToken}";
        if (key is not null)
        {
            context.Request.Headers["Idempotency-Key"] = key;
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:PublicOrigin"] = "https://shop.greygray.test",
                ["Storefront:PublicApiOrigin"] = "https://api.greygray.test",
            })
            .Build();

    private static OrderView Order(
        CustomerId customerId,
        OrderStatus status,
        DateTimeOffset? paymentDueAt) =>
        new(
            OrderId.New(),
            customerId,
            SourceChannel.Own,
            status,
            ShippingPolicy.HoldUntilComplete,
            PricingSnapshotId.New(),
            new Money(1_000, Currency.TWD),
            new Money(600, Currency.TWD),
            new Money(1_600, Currency.TWD),
            [],
            Now.AddDays(-1))
        {
            OrderNumber = "GG202610020001",
            DeliveryMethod = DeliveryMethod.HomeDelivery,
            PaymentDueAt = paymentDueAt,
            QuoteExplain = ["測試報價"],
        };

    private static PaymentInstructionsView Instructions() =>
        new(
            PaymentMethod.Atm,
            "822",
            "12345678901234",
            null,
            [],
            Now.AddDays(1),
            Now.AddHours(-1));

    private static FakeCatalogQuery Catalog() =>
        new(new SkuSnapshot(
            SkuId.New(),
            ProductId.New(),
            "測試商品",
            null,
            100,
            new Dimensions(1, 1, 1),
            true));

    private static FakeCustomerDirectory CustomerDirectory(CustomerId customerId) =>
        new(new CustomerSummary(customerId, "灰灰", MemberTier.Standard, true));

    private sealed class RecordingInstructionsQuery(PaymentInstructionsView? instructions)
        : IPaymentInstructionsQuery
    {
        public int Calls { get; private set; }

        public Task<Result<PaymentInstructionsView?>> GetOutstandingInstructionsAsync(
            OrderId orderId,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result<PaymentInstructionsView?>.Success(instructions));
        }
    }

    private sealed class RecordingPaymentCommand : IPaymentCommand
    {
        public int InitiateCalls { get; private set; }

        public PaymentInitiationRequest? LastRequest { get; private set; }

        public Task<Result<PaymentInitiation>> InitiateAsync(
            PaymentInitiationRequest request,
            CancellationToken cancellationToken)
        {
            InitiateCalls++;
            LastRequest = request;
            return Task.FromResult(Result<PaymentInitiation>.Success(new PaymentInitiation(
                PaymentProvider.ECPay,
                "POST",
                new Uri("https://payment.greygray.test"),
                new Dictionary<string, string>(),
                Now.AddMinutes(20))));
        }

        public Task<Result> HandleEcpayCallbackAsync(
            IReadOnlyDictionary<string, string> fields,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubOrderingApplication(OrderView order) : IOrderingApplication
    {
        public OrderView? CancelledResult { get; init; }

        public int CancelCustomerCalls { get; private set; }

        public Task<Result<OrderView>> GetCustomerAsync(
            CustomerId customerId,
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(customerId == order.CustomerId && orderId == order.Id
                ? Result<OrderView>.Success(order)
                : Result<OrderView>.Failure("ordering.order-not-found", "找不到訂單。"));

        public Task<Result<OrderView>> CancelCustomerAsync(
            CustomerId customerId,
            OrderId orderId,
            string? reason,
            CancellationToken cancellationToken)
        {
            CancelCustomerCalls++;
            return Task.FromResult(Result<OrderView>.Success(CancelledResult ?? order));
        }

        public Task<Result<OrderView>> CreateFromCheckoutAsync(CheckoutCompleted checkout, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OrderPage<OrderView>>> ListCustomerAsync(CustomerOrderListRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OrderPage<OrderView>>> ListAdminAsync(AdminOrderListRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OrderView>> GetAdminAsync(OrderId orderId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OrderView>> CancelAdminAsync(OrderId orderId, string reason, RefundDestination refundTo, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OrderView>> CancelLineAsync(OrderId orderId, OrderLineId lineId, string reason, RefundDestination refundTo, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OrderView>> RefundLineShortfallAsync(OrderId orderId, OrderLineId lineId, string reason, RefundDestination refundTo, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> RecordPaymentCapturedAsync(OrderId orderId, Money amount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> RecordPaymentFailedAsync(OrderId orderId, string failureCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> RecordPaymentRefundedAsync(OrderId orderId, Money amount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> RecordItemPurchasedAsync(OrderLineId orderLineId, int quantityPurchased, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
