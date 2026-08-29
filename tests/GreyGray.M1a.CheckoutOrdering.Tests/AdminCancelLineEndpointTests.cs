using GreyGray.Api.Admin;
using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

public sealed class AdminCancelLineEndpointTests
{
    private const string StaffItem = "greygray.staff-id";

    [Fact(DisplayName = "取消品項相同 Idempotency-Key 重送只執行一次退款命令")]
    public async Task Same_idempotency_key_runs_line_cancel_once()
    {
        var (order, sku, customer) = Scenario(paid: true);
        var ordering = new StubOrderingApplication(order);
        var idempotency = new MemoryIdempotencyStore();
        var staffId = StaffId.New();
        var input = new M1aEndpoints.CancelOrderInput(
            "現場缺貨",
            RefundDestination.StoredValue);

        await M1aEndpoints.CancelOrderLineAsync(
            order.Id.ToString(),
            order.Lines[0].Id.ToString(),
            input,
            Context("same-key", staffId),
            ordering,
            new FakeCustomerDirectory(customer),
            new FakePaymentQuery(),
            new FakeCatalogQuery(sku),
            idempotency,
            TestContext.Current.CancellationToken);
        await M1aEndpoints.CancelOrderLineAsync(
            order.Id.ToString(),
            order.Lines[0].Id.ToString(),
            input,
            Context("same-key", staffId),
            ordering,
            new FakeCustomerDirectory(customer),
            new FakePaymentQuery(),
            new FakeCatalogQuery(sku),
            idempotency,
            TestContext.Current.CancellationToken);

        ordering.CancelLineCalls.ShouldBe(1);
    }

    [Fact(DisplayName = "已付款品項選原路退款回 422 與 frozen code，且訂單不變")]
    public async Task Paid_original_method_refund_fails_before_mutation()
    {
        var (order, sku, customer) = Scenario(paid: true);
        var ordering = new StubOrderingApplication(order);
        var context = Context("original-refund");
        var result = await M1aEndpoints.CancelOrderLineAsync(
            order.Id.ToString(),
            order.Lines[0].Id.ToString(),
            new M1aEndpoints.CancelOrderInput(
                "現場缺貨",
                RefundDestination.OriginalPaymentMethod),
            context,
            ordering,
            new FakeCustomerDirectory(customer),
            new FakePaymentQuery(),
            new FakeCatalogQuery(sku),
            new MemoryIdempotencyStore(),
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("payment.original-refund-not-configured");
        ordering.CancelLineCalls.ShouldBe(0);
        ordering.Current.Lines[0].Status.ShouldBe(OrderLineStatus.Pending);
    }

    private static DefaultHttpContext Context(string key, StaffId? staffId = null)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Request.Headers["Idempotency-Key"] = key;
        context.Items[StaffItem] = staffId ?? StaffId.New();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static (OrderView Order, SkuSnapshot Sku, CustomerSummary Customer) Scenario(bool paid)
    {
        var sku = new SkuSnapshot(
            SkuId.New(),
            ProductId.New(),
            "缺貨商品",
            "單入",
            100,
            new Dimensions(10, 10, 10),
            true);
        var customer = new CustomerSummary(
            CustomerId.New(),
            "測試客戶",
            MemberTier.Standard,
            true);
        var line = new OrderLineView(
            OrderLineId.New(),
            sku.Id,
            FulfillmentMode.Preorder,
            OrderLineStatus.Pending,
            1,
            Money.OfMajor(100, Currency.TWD),
            CampaignId.New(),
            null);
        var order = new OrderView(
            OrderId.New(),
            customer.Id,
            SourceChannel.Own,
            paid ? OrderStatus.PaidAwaitingClose : OrderStatus.AwaitingPayment,
            ShippingPolicy.HoldUntilComplete,
            PricingSnapshotId.New(),
            line.LineTotal,
            Money.OfMajor(60, Currency.TWD),
            Money.OfMajor(160, Currency.TWD),
            [line],
            DateTimeOffset.UnixEpoch)
        {
            OrderNumber = "GG2608290000001",
            DeliveryMethod = DeliveryMethod.ConvenienceStore,
            PaidAmount = paid ? Money.OfMajor(160, Currency.TWD) : null,
        };
        return (order, sku, customer);
    }

    private sealed class StubOrderingApplication(OrderView current) : IOrderingApplication
    {
        public OrderView Current { get; private set; } = current;

        public int CancelLineCalls { get; private set; }

        public Task<Result<OrderView>> GetAdminAsync(
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(orderId == Current.Id
                ? Result<OrderView>.Success(Current)
                : Result<OrderView>.Failure("ordering.order-not-found", "找不到訂單。"));

        public Task<Result<OrderView>> CancelLineAsync(
            OrderId orderId,
            OrderLineId lineId,
            string reason,
            RefundDestination refundTo,
            CancellationToken cancellationToken)
        {
            CancelLineCalls++;
            var line = Current.Lines.Single(candidate => candidate.Id == lineId);
            Current = Current with
            {
                GoodsTotal = Current.GoodsTotal - line.LineTotal,
                GrandTotal = Current.GrandTotal - line.LineTotal,
                Lines = Current.Lines.Select(candidate => candidate.Id == lineId
                    ? candidate with
                    {
                        Status = OrderLineStatus.Unavailable,
                        RefundedAmount = candidate.LineTotal,
                    }
                    : candidate).ToArray(),
            };
            return Task.FromResult(Result<OrderView>.Success(Current));
        }

        public Task<Result<OrderView>> CreateFromCheckoutAsync(
            CheckoutCompleted checkout,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OrderPage<OrderView>>> ListCustomerAsync(
            CustomerOrderListRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OrderView>> GetCustomerAsync(
            CustomerId customerId,
            OrderId orderId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OrderView>> CancelCustomerAsync(
            CustomerId customerId,
            OrderId orderId,
            string? reason,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OrderPage<OrderView>>> ListAdminAsync(
            AdminOrderListRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OrderView>> CancelAdminAsync(
            OrderId orderId,
            string reason,
            RefundDestination refundTo,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> RecordPaymentCapturedAsync(
            OrderId orderId,
            Money amount,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> RecordPaymentFailedAsync(
            OrderId orderId,
            string failureCode,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> RecordPaymentRefundedAsync(
            OrderId orderId,
            Money amount,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> RecordItemPurchasedAsync(
            OrderLineId orderLineId,
            int quantityPurchased,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class MemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly Dictionary<(string Key, string Scope), Entry> _entries = [];

        public Task<(IdempotencyOutcome Outcome, string? cachedResponse)> TryBeginAsync(
            string key,
            string scope,
            string requestHash,
            CancellationToken cancellationToken)
        {
            var identity = (key, scope);
            if (!_entries.TryGetValue(identity, out var entry))
            {
                _entries.Add(identity, new Entry(requestHash, null));
                return Task.FromResult((IdempotencyOutcome.Proceed, (string?)null));
            }

            if (!StringComparer.Ordinal.Equals(entry.RequestHash, requestHash))
            {
                return Task.FromResult((IdempotencyOutcome.KeyReusedWithDifferentPayload, (string?)null));
            }

            return entry.Response is null
                ? Task.FromResult<(IdempotencyOutcome Outcome, string? cachedResponse)>(
                    (IdempotencyOutcome.InFlight, null))
                : Task.FromResult<(IdempotencyOutcome Outcome, string? cachedResponse)>(
                    (IdempotencyOutcome.AlreadyCompleted, entry.Response));
        }

        public Task CompleteAsync(
            string key,
            string scope,
            string responseSnapshot,
            CancellationToken cancellationToken)
        {
            var entry = _entries[(key, scope)];
            _entries[(key, scope)] = entry with { Response = responseSnapshot };
            return Task.CompletedTask;
        }

        public Task AbandonAsync(
            string key,
            string scope,
            CancellationToken cancellationToken)
        {
            _entries.Remove((key, scope));
            return Task.CompletedTask;
        }

        private sealed record Entry(string RequestHash, string? Response);
    }
}
