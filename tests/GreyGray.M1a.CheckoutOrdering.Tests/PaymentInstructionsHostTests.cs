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
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;
using GreyGray.Shared.Kernel.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

public sealed class PaymentInstructionsHostTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 4, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "O1：待付款訂單詳情帶出完整取號資訊")]
    public async Task Awaiting_payment_order_returns_payment_instructions()
    {
        var customer = CustomerId.New();
        var order = Order(customer, OrderStatus.AwaitingPayment);
        var instructions = Instructions(PaymentMethod.Atm);
        var query = new RecordingInstructionsQuery(Result<PaymentInstructionsView?>.Success(instructions));

        using var document = await GetOrderAsync(order, query);

        query.Calls.ShouldBe(1);
        query.LastOrderId.ShouldBe(order.Id);
        var paymentInstructions = document.RootElement.GetProperty("paymentInstructions");
        paymentInstructions.GetProperty("method").GetString().ShouldBe("Atm");
        paymentInstructions.GetProperty("bankCode").GetString().ShouldBe("822");
        paymentInstructions.GetProperty("virtualAccount").GetString().ShouldBe("12345678901234");
        paymentInstructions.GetProperty("paymentNo").GetString().ShouldBe("CVS12345678");
        paymentInstructions.GetProperty("barcodes")[0].GetString().ShouldBe("BARCODE-1");
        paymentInstructions.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(instructions.ExpiresAt);
        paymentInstructions.GetProperty("issuedAt").GetDateTimeOffset().ShouldBe(instructions.IssuedAt);
    }

    [Theory(DisplayName = "O2：非待付款訂單固定回 null 且不查取號資訊")]
    [InlineData(OrderStatus.PaidAwaitingClose)]
    [InlineData(OrderStatus.ClosedAwaitingDeparture)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Completed)]
    public async Task Non_awaiting_payment_order_skips_payment_instructions_query(OrderStatus status)
    {
        var order = Order(CustomerId.New(), status);
        var query = new RecordingInstructionsQuery(
            Result<PaymentInstructionsView?>.Success(Instructions(PaymentMethod.Barcode)));

        using var document = await GetOrderAsync(order, query);

        query.Calls.ShouldBe(0);
        document.RootElement.GetProperty("paymentInstructions").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory(DisplayName = "O4：待付款但付款失敗或取號過期時回 null")]
    [InlineData("付款失敗後已無有效取號")]
    [InlineData("上一組取號已過期")]
    public async Task Awaiting_payment_without_outstanding_instructions_returns_null(string scenario)
    {
        _ = scenario;
        var order = Order(CustomerId.New(), OrderStatus.AwaitingPayment);
        var query = new RecordingInstructionsQuery(Result<PaymentInstructionsView?>.Success(null));

        using var document = await GetOrderAsync(order, query);

        query.Calls.ShouldBe(1);
        document.RootElement.GetProperty("paymentInstructions").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact(DisplayName = "O5：待付款取號查詢失敗回 Problem，不把失敗偽裝成 null")]
    public async Task Payment_instructions_query_failure_returns_problem()
    {
        var customer = CustomerId.New();
        var order = Order(customer, OrderStatus.AwaitingPayment);
        var sessions = new StorefrontCheckoutEndpointTests.FakeSessionStore();
        var context = Context(sessions.Issue(customer));
        var query = new RecordingInstructionsQuery(
            Result<PaymentInstructionsView?>.Failure("payment.instructions-query-failed", "取號查詢失敗。"));

        var result = await StorefrontEndpoints.GetCustomerOrderAsync(
            order.Id.ToString(),
            context,
            sessions,
            new StubOrderingApplication(order),
            Catalog(),
            CustomerDirectory(customer),
            query,
            new FakeClock(Now),
            NullLogger.Instance,
            TestContext.Current.CancellationToken);
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        body.ShouldContain("payment.instructions-query-failed");
    }

    [Fact(DisplayName = "O3：結帳與取消共用的 ToOrderAsync 簽章不變且預設取號欄位為 null")]
    public async Task Existing_order_render_path_keeps_signature_and_null_instructions()
    {
        var method = typeof(StorefrontEndpoints).GetMethod(
            "ToOrderAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.ShouldNotBeNull();
        method.GetParameters().Select(parameter => parameter.ParameterType).ShouldBe(
            [
                typeof(OrderView),
                typeof(ICatalogQuery),
                typeof(ICustomerDirectory),
                typeof(Microsoft.Extensions.Logging.ILogger),
                typeof(CancellationToken),
            ]);
        var customer = CustomerId.New();
        var task = (Task)method.Invoke(
            null,
            [
                Order(customer, OrderStatus.AwaitingPayment),
                Catalog(),
                CustomerDirectory(customer),
                NullLogger.Instance,
                TestContext.Current.CancellationToken,
            ])!;

        await task;
        var response = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var json = JsonSerializer.Serialize(response, response.GetType(), GreyGrayJson.Options);
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("paymentInstructions").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact(DisplayName = "A1／C1：Admin 保留付款順序與既有欄位，並逐筆投影 method／instructions")]
    public async Task Admin_order_projects_each_payment_method_and_instructions_without_reordering()
    {
        var customer = CustomerId.New();
        var order = Order(customer, OrderStatus.PaidAwaitingClose);
        var failedId = PaymentId.New();
        var capturedId = PaymentId.New();
        var creditId = PaymentId.New();
        var failed = new PaymentSummary(
            failedId, order.Id, PaymentProvider.ECPay, PaymentStatus.Failed,
            new Money(1_600, Currency.TWD), null, "failed-trade", null, null,
            PaymentMethod.ConvenienceStoreCode, null);
        var capturedInstructions = Instructions(PaymentMethod.Atm);
        var captured = new PaymentSummary(
            capturedId, order.Id, PaymentProvider.ECPay, PaymentStatus.Captured,
            new Money(1_600, Currency.TWD), new Money(30, Currency.TWD), "captured-trade", Now, Now,
            PaymentMethod.Atm, capturedInstructions);
        var oldCredit = new PaymentSummary(
            creditId, order.Id, PaymentProvider.ECPay, PaymentStatus.Captured,
            new Money(1_600, Currency.TWD), null, "credit-trade", Now.AddDays(-1), null,
            PaymentMethod.CreditCard, null);
        var payments = new StubPaymentQuery([failed, captured, oldCredit]);

        var response = await AdminEndpoints.ToAdminOrderAsync(
            order,
            CustomerDirectory(customer),
            payments,
            Catalog(),
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        response.Id.ShouldBe(order.Id);
        response.Status.ShouldBe(order.Status);
        response.GrandTotal.ShouldBe(order.GrandTotal);
        response.Payments.Count.ShouldBe(3);
        response.Payments.Select(payment => payment.Id).ShouldBe([failedId, capturedId, creditId]);
        response.Payments[0].ProviderTransactionId.ShouldBe("failed-trade");
        response.Payments[0].Method.ShouldBe(PaymentMethod.ConvenienceStoreCode);
        response.Payments[0].Instructions.ShouldBeNull();
        response.Payments[1].Fee.ShouldBe(new Money(30, Currency.TWD));
        response.Payments[1].CapturedAt.ShouldBe(Now);
        response.Payments[1].SettledAt.ShouldBe(Now);
        response.Payments[1].Method.ShouldBe(PaymentMethod.Atm);
        var projectedInstructions = response.Payments[1].Instructions;
        projectedInstructions.ShouldNotBeNull();
        projectedInstructions!.Method.ShouldBe(PaymentMethod.Atm);
        projectedInstructions.VirtualAccount.ShouldBe("12345678901234");
        response.Payments[2].Method.ShouldBe(PaymentMethod.CreditCard);
        response.Payments[2].Instructions.ShouldBeNull();
    }

    private static async Task<JsonDocument> GetOrderAsync(
        OrderView order,
        IPaymentInstructionsQuery query)
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
            new FakeClock(Now),
            NullLogger.Instance,
            TestContext.Current.CancellationToken);
        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static DefaultHttpContext Context(string sessionToken)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpJsonOptions(options =>
            BffHttp.ApplyGreyGrayJson(options.SerializerOptions));
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        context.Request.Headers.Cookie = $"gg_session={sessionToken}";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static OrderView Order(CustomerId customerId, OrderStatus status) =>
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
            Now)
        {
            OrderNumber = "GG202610010001",
            DeliveryMethod = DeliveryMethod.HomeDelivery,
            PaymentDueAt = Now.AddDays(1),
            QuoteExplain = ["測試報價"],
        };

    private static PaymentInstructionsView Instructions(PaymentMethod method) =>
        new(
            method,
            "822",
            "12345678901234",
            "CVS12345678",
            ["BARCODE-1", "BARCODE-2", "BARCODE-3"],
            Now.AddDays(2),
            Now);

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

    private sealed class RecordingInstructionsQuery(Result<PaymentInstructionsView?> result)
        : IPaymentInstructionsQuery
    {
        public int Calls { get; private set; }

        public OrderId? LastOrderId { get; private set; }

        public Task<Result<PaymentInstructionsView?>> GetOutstandingInstructionsAsync(
            OrderId orderId,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastOrderId = orderId;
            return Task.FromResult(result);
        }
    }

    private sealed class StubPaymentQuery(IReadOnlyList<PaymentSummary> payments) : IPaymentQuery
    {
        public Task<Result<PaymentSummary>> GetAsync(PaymentId id, CancellationToken cancellationToken) =>
            Task.FromResult(Result<PaymentSummary>.Failure("payment.not-found", "找不到付款。"));

        public Task<Result<IReadOnlyList<PaymentSummary>>> GetByOrderAsync(
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<PaymentSummary>>.Success(payments));

        public Task<Result<IReadOnlyList<ProviderCapability>>> GetEnabledProvidersAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<ProviderCapability>>.Success([]));
    }

    private sealed class StubOrderingApplication(OrderView order) : IOrderingApplication
    {
        public Task<Result<OrderView>> GetCustomerAsync(
            CustomerId customerId,
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(customerId == order.CustomerId && orderId == order.Id
                ? Result<OrderView>.Success(order)
                : Result<OrderView>.Failure("ordering.order-not-found", "找不到訂單。"));

        public Task<Result<OrderView>> CreateFromCheckoutAsync(CheckoutCompleted checkout, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<OrderPage<OrderView>>> ListCustomerAsync(CustomerOrderListRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<OrderView>> CancelCustomerAsync(CustomerId customerId, OrderId orderId, string? reason, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<OrderPage<OrderView>>> ListAdminAsync(AdminOrderListRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<OrderView>> GetAdminAsync(OrderId orderId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<OrderView>> CancelAdminAsync(OrderId orderId, string reason, RefundDestination refundTo, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<OrderView>> CancelLineAsync(OrderId orderId, OrderLineId lineId, string reason, RefundDestination refundTo, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<OrderView>> RefundLineShortfallAsync(OrderId orderId, OrderLineId lineId, string reason, RefundDestination refundTo, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> RecordPaymentCapturedAsync(OrderId orderId, Money amount, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> RecordPaymentFailedAsync(OrderId orderId, string failureCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> RecordPaymentRefundedAsync(OrderId orderId, Money amount, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> RecordItemPurchasedAsync(OrderLineId orderLineId, int quantityPurchased, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
