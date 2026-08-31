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
using Microsoft.Extensions.Logging;
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
            new CapturingLogger(),
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
            new CapturingLogger(),
            TestContext.Current.CancellationToken);

        ordering.CancelLineCalls.ShouldBe(1);
    }

    // BE-30：這條測試原本斷言「已付款 ＋ 原路退款 → 422、不呼叫 application service」。
    // 那個守衛寫在 M1a-6，當時綠界退刷 API 還沒做，是合理的 fail-closed；BE-17／BE-18
    // 把退款流程做完之後它變成路障，已從 M1aEndpoints 拿掉，所以這裡改成驗證新行為：
    // 原路退款要真的打到 application service，由那一層決定成敗。
    [Fact(DisplayName = "已付款品項選原路退款會打到 application service 並回 200")]
    public async Task Paid_original_method_refund_reaches_application_service()
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
            new CapturingLogger(),
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        ordering.CancelLineCalls.ShouldBe(1);
        ordering.Current.Lines[0].Status.ShouldBe(OrderLineStatus.Unavailable);
        ordering.Current.Lines[0].RefundedAmount.ShouldBe(order.Lines[0].LineTotal);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain("payment.original-refund-not-configured");
    }

    // BE-35／A4：退款事件在 CancelLineAsync 就已經送出去了，之後的 ToAdminOrderAsync 只是組回應。
    // 組回應讀不到別的模組時不可以把已經完成的取消報成失敗，也不可以 abandon 冪等鍵——底層是
    // 品項狀態守衛（已 Unavailable 就擋下），abandon 之後重試回不了成功，退款卻已經發生。
    [Fact(DisplayName = "BE-35 A4：退款已送出時，組回應讀不到客戶也要回 200 且冪等鍵不是 Abandoned")]
    public async Task Committed_line_cancel_is_not_reported_as_a_failure()
    {
        var (order, sku, _) = Scenario(paid: true);
        var ordering = new StubOrderingApplication(order);
        var idempotency = new InspectableIdempotencyStore();
        var logger = new CapturingLogger();
        var context = Context("committed-line-cancel");
        var scope = M1aEndpoints.Scope(
            context,
            $"orders:{order.Id}:lines:{order.Lines[0].Id}:cancel");

        // 組回應時 customers.GetAsync 會失敗：directory 裡登記的是另一個人。
        var strangerDirectory = new FakeCustomerDirectory(
            new CustomerSummary(CustomerId.New(), "別人", MemberTier.Standard, true));
        var result = await M1aEndpoints.CancelOrderLineAsync(
            order.Id.ToString(),
            order.Lines[0].Id.ToString(),
            new M1aEndpoints.CancelOrderInput("現場缺貨", RefundDestination.StoredValue),
            context,
            ordering,
            strangerDirectory,
            new FakePaymentQuery(),
            new FakeCatalogQuery(sku),
            idempotency,
            logger,
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        ordering.CancelLineCalls.ShouldBe(1, "副作用確實已經產生。");
        idempotency.StatusOf("committed-line-cancel", scope).ShouldBe(
            InspectableIdempotencyStore.EntryStatus.Completed,
            "副作用已 commit 就不准 abandon。");

        // 退化回應仍符合契約：customerDisplayName 是必填，退成 customerId 而不是 null。
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        body.ShouldContain($"\"customerDisplayName\":\"{order.CustomerId}\"");
        logger.Entries.ShouldContain(
            entry => entry.Level == LogLevel.Error && entry.Message.Contains("讀不到客戶"),
            "退化不留痕就是把故障藏起來。");
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

        public Task<Result<OrderView>> RefundLineShortfallAsync(
            OrderId orderId,
            OrderLineId lineId,
            string reason,
            RefundDestination refundTo,
            CancellationToken cancellationToken) => throw new NotSupportedException();
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
