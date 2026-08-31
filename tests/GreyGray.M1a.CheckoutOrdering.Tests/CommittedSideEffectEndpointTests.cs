using System.Text.Json;
using System.Text.RegularExpressions;
using GreyGray.Api.Admin;
using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using AdminEndpoints = GreyGray.Api.Admin.M1aEndpoints;
using FakeSessionStore = GreyGray.M1a.CheckoutOrdering.Tests.StorefrontCheckoutEndpointTests.FakeSessionStore;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;
using StorefrontEndpoints = GreyGray.Api.Storefront.M1aEndpoints;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

/// <summary>
/// BE-35：「副作用已經 commit，之後才在組回應那一步失敗」的迴歸網。
/// </summary>
/// <remarks>
/// <para>
/// BE-34 的分類表把這個形狀的端點找齊了，共五個：S11 <c>POST /v1/cart/checkout</c>、
/// S12 <c>POST /v1/orders/{orderId}/cancel</c>、A3 <c>POST /v1/orders/{orderId}/cancel</c>（Admin）、
/// A4 <c>POST /v1/orders/{orderId}/lines/{lineId}/cancel</c>、
/// A20 <c>POST /v1/orders/{id}/lines/{lineId}/refund-shortfall</c>。
/// S11 在 <see cref="StorefrontCheckoutEndpointTests"/>、A4 在
/// <see cref="AdminCancelLineEndpointTests"/>，剩下三個在這裡。
/// </para>
/// <para>
/// 每一條斷言的都是同一組不變式：<b>副作用已產生時，回應是成功狀態碼，冪等鍵不是
/// <c>Abandoned</c>，退化回應仍符合契約，而且退化留下了 log</b>。
/// S12／A3／A20 比 checkout 更嚴重——底層是狀態機守衛，abandon 之後重試回不了成功，
/// 而退款事件已經送出去了。
/// </para>
/// </remarks>
public sealed class CommittedSideEffectEndpointTests
{
    private const string StaffItem = "greygray.staff-id";

    // ── S12：POST /v1/orders/{orderId}/cancel（前台）──────────────────────────

    [Fact(DisplayName = "BE-35 S12：取消已送出退款時，組回應讀不到 SKU 也要回 200 且冪等鍵不是 Abandoned")]
    public async Task Committed_customer_cancel_is_not_reported_as_a_failure()
    {
        var (order, sku, customer) = Scenario();
        var ordering = new StubOrderingApplication(order);
        var idempotency = new InspectableIdempotencyStore();
        var logger = new CapturingLogger();
        var sessions = new FakeSessionStore();
        var token = sessions.Issue(customer.Id);
        var context = StorefrontContext("cancel-key", token);

        // 組回應時 catalog 讀不到這張訂單的 SKU：目錄裡登記的是另一個商品。
        var brokenCatalog = new FakeCatalogQuery(OtherSku());
        var result = await StorefrontEndpoints.CancelCustomerOrderAsync(
            order.Id.ToString(),
            new StorefrontEndpoints.CancelOrderInput("不想要了"),
            context,
            sessions,
            ordering,
            brokenCatalog,
            new FakeCustomerDirectory(customer),
            idempotency,
            logger,
            TestContext.Current.CancellationToken);

        var body = await ReadAsync(result, context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        ordering.CancelCustomerCalls.ShouldBe(1, "取消與 RefundRequested 確實已經送出去了。");
        idempotency.StatusOf("cancel-key", "storefront:orders:cancel").ShouldBe(
            InspectableIdempotencyStore.EntryStatus.Completed,
            "副作用已 commit 就不准 abandon——底層是狀態機守衛，重試回不了成功。");

        // 退化回應仍符合契約（docs/api/openapi.storefront.yaml 的 Order／OrderLine）：
        // 必填欄位一個都不能缺、一個都不能是 null。
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        ShouldHaveRequiredFields(
            root,
            "id",
            "orderNumber",
            "status",
            "shippingPolicy",
            "deliveryMethod",
            "goodsTotal",
            "shippingFee",
            "grandTotal",
            "lines",
            "placedAt");
        var line = root.GetProperty("lines").EnumerateArray().ShouldHaveSingleItem();
        ShouldHaveRequiredFields(
            line,
            "id",
            "skuId",
            "productId",
            "name",
            "mode",
            "status",
            "quantity",
            "unitPrice",
            "lineTotal");
        line.GetProperty("name").GetString().ShouldBe(
            sku.Id.ToString(),
            "商品名讀不到就用 SkuId 當顯示名，不可以留空。");
        ShouldBeAnId(line.GetProperty("productId"), "productId");
        logger.Entries.ShouldContain(
            entry => entry.Level == LogLevel.Error && entry.Message.Contains("讀不到 SKU"));
    }

    // ── A3：POST /v1/orders/{orderId}/cancel（後台）──────────────────────────

    [Fact(DisplayName = "BE-35 A3：取消已送出退款時，組回應讀不到 SKU 也要回 200 且冪等鍵不是 Abandoned")]
    public async Task Committed_admin_cancel_is_not_reported_as_a_failure()
    {
        var (order, sku, customer) = Scenario();
        var ordering = new StubOrderingApplication(order);
        var idempotency = new InspectableIdempotencyStore();
        var logger = new CapturingLogger();
        var context = AdminContext("admin-cancel-key");
        var scope = AdminEndpoints.Scope(context, $"orders:{order.Id}:cancel");

        var result = await AdminEndpoints.CancelOrderAsync(
            order.Id.ToString(),
            new AdminEndpoints.CancelOrderInput("客訴全退", RefundDestination.StoredValue),
            context,
            ordering,
            new FakeCustomerDirectory(customer),
            new FakePaymentQuery(),
            new FakeCatalogQuery(OtherSku()),
            idempotency,
            logger,
            TestContext.Current.CancellationToken);

        var body = await ReadAsync(result, context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        ordering.CancelAdminCalls.ShouldBe(1, "RefundRequested 已經送出去了。");
        idempotency.StatusOf("admin-cancel-key", scope).ShouldBe(
            InspectableIdempotencyStore.EntryStatus.Completed);

        // 退化回應仍符合契約（docs/api/openapi.admin.yaml 的 AdminOrder／AdminOrderLine）。
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        ShouldHaveRequiredFields(
            root,
            "id",
            "orderNumber",
            "customerDisplayName",
            "status",
            "grandTotal",
            "placedAt",
            "lines",
            "goodsTotal",
            "shippingFee",
            "deliveryMethod",
            "shippingPolicy");
        var line = root.GetProperty("lines").EnumerateArray().ShouldHaveSingleItem();
        ShouldHaveRequiredFields(
            line,
            "id",
            "skuId",
            "name",
            "mode",
            "status",
            "quantity",
            "unitPrice",
            "lineTotal");
        line.GetProperty("name").GetString().ShouldBe(
            sku.Id.ToString(),
            "商品名讀不到就用 SkuId 當顯示名，不可以留空。");
        logger.Entries.ShouldContain(
            entry => entry.Level == LogLevel.Error && entry.Message.Contains("讀不到商品資料"));
    }

    // ── A20：POST /v1/orders/{id}/lines/{lineId}/refund-shortfall ─────────────

    [Fact(DisplayName = "BE-35 A20：短缺退款已送出時，組回應讀不到付款也要回 200 且冪等鍵不是 Abandoned")]
    public async Task Committed_shortfall_refund_is_not_reported_as_a_failure()
    {
        var (order, sku, customer) = Scenario();
        var ordering = new StubOrderingApplication(order);
        var idempotency = new InspectableIdempotencyStore();
        var logger = new CapturingLogger();
        var context = AdminContext("shortfall-key");
        var scope = AdminEndpoints.Scope(
            context,
            $"orders:{order.Id}:lines:{order.Lines[0].Id}:refund-shortfall");

        var result = await M1bShortfallRefundEndpoints.RefundLineShortfallAsync(
            order.Id.ToString(),
            order.Lines[0].Id.ToString(),
            new AdminEndpoints.CancelOrderInput("短缺 2 件", RefundDestination.StoredValue),
            context,
            ordering,
            new FakeCustomerDirectory(customer),
            new FailingPaymentQuery(),
            new FakeCatalogQuery(sku),
            idempotency,
            logger,
            TestContext.Current.CancellationToken);

        var body = await ReadAsync(result, context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        ordering.RefundShortfallCalls.ShouldBe(1, "退款事件已經送出去了。");
        idempotency.StatusOf("shortfall-key", scope).ShouldBe(
            InspectableIdempotencyStore.EntryStatus.Completed);

        // 退化回應仍符合契約：payments 不是必填欄位，但既然寫出來就得是陣列，
        // 不可以是 null（契約寫的是 array of PaymentSummary）。
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        ShouldHaveRequiredFields(
            root,
            "id",
            "orderNumber",
            "customerDisplayName",
            "status",
            "grandTotal",
            "placedAt",
            "lines",
            "goodsTotal",
            "shippingFee",
            "deliveryMethod",
            "shippingPolicy");
        root.GetProperty("payments").ValueKind.ShouldBe(JsonValueKind.Array);
        root.GetProperty("payments").GetArrayLength().ShouldBe(0);
        logger.Entries.ShouldContain(
            entry => entry.Level == LogLevel.Error && entry.Message.Contains("讀不到付款紀錄"));
    }

    // ── 共用 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 逐一檢查契約列為必填的欄位：<b>有出現、而且不是 null</b>。
    /// 退化回應最容易犯的錯就是塞 null 進不可為 null 的欄位，那等於偷偷改契約。
    /// </summary>
    private static void ShouldHaveRequiredFields(JsonElement element, params string[] required)
    {
        foreach (var name in required)
        {
            element.TryGetProperty(name, out var value).ShouldBeTrue($"契約必填欄位 {name} 不見了。");
            value.ValueKind.ShouldNotBe(JsonValueKind.Null, $"契約必填欄位 {name} 不可以是 null。");
        }
    }

    /// <summary>契約的 <c>Id</c> schema：32 字元小寫十六進位，無連字號。</summary>
    private static void ShouldBeAnId(JsonElement element, string fieldName)
    {
        var value = element.GetString();
        value.ShouldNotBeNull();
        Regex.IsMatch(value, "^[0-9a-f]{32}$").ShouldBeTrue(
            $"{fieldName} 的退化值必須仍然符合契約 Id 的 pattern，實際是「{value}」。");
    }

    private static async Task<string> ReadAsync(IResult result, DefaultHttpContext context)
    {
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    private static DefaultHttpContext AdminContext(string key)
    {
        var context = NewContext(key);
        context.Items[StaffItem] = StaffId.New();
        return context;
    }

    private static DefaultHttpContext StorefrontContext(string key, string sessionToken)
    {
        var context = NewContext(key);
        context.Request.Headers.Cookie = $"gg_session={sessionToken}";
        return context;
    }

    private static DefaultHttpContext NewContext(string key)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Request.Headers["Idempotency-Key"] = key;
        context.Response.Body = new MemoryStream();
        return context;
    }

    /// <summary>目錄裡「另一個」商品——用它讓組回應那一步的 catalog 查詢必定失敗。</summary>
    private static SkuSnapshot OtherSku() => new(
        SkuId.New(),
        ProductId.New(),
        "別的商品",
        null,
        100,
        new Dimensions(10, 10, 10),
        true);

    private static (OrderView Order, SkuSnapshot Sku, CustomerSummary Customer) Scenario()
    {
        var sku = new SkuSnapshot(
            SkuId.New(),
            ProductId.New(),
            "面膜",
            "10 入",
            300,
            new Dimensions(20, 15, 5),
            true);
        var customer = new CustomerSummary(CustomerId.New(), "灰灰", MemberTier.Standard, true);
        var line = new OrderLineView(
            OrderLineId.New(),
            sku.Id,
            FulfillmentMode.Preorder,
            OrderLineStatus.Pending,
            2,
            Money.OfMajor(100, Currency.TWD),
            CampaignId.New(),
            null)
        {
            QuantityShortfall = 1,
        };
        var order = new OrderView(
            OrderId.New(),
            customer.Id,
            SourceChannel.Own,
            OrderStatus.PaidAwaitingClose,
            ShippingPolicy.HoldUntilComplete,
            PricingSnapshotId.New(),
            line.LineTotal,
            Money.OfMajor(60, Currency.TWD),
            line.LineTotal + Money.OfMajor(60, Currency.TWD),
            [line],
            DateTimeOffset.UnixEpoch)
        {
            OrderNumber = "GG2609010000001",
            DeliveryMethod = DeliveryMethod.ConvenienceStore,
            PaidAmount = line.LineTotal + Money.OfMajor(60, Currency.TWD),
        };
        return (order, sku, customer);
    }

    /// <summary>付款查詢永遠失敗——A20 用它把「組回應那一步壞掉」做成確定性的。</summary>
    private sealed class FailingPaymentQuery : IPaymentQuery
    {
        public Task<Result<PaymentSummary>> GetAsync(
            PaymentId id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<PaymentSummary>.Failure("payment.not-found", "找不到付款。"));

        public Task<Result<IReadOnlyList<PaymentSummary>>> GetByOrderAsync(
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<PaymentSummary>>.Failure(
                "payment.unavailable",
                "付款資料暫時讀不到。"));

        public Task<Result<IReadOnlyList<ProviderCapability>>> GetEnabledProvidersAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<ProviderCapability>>.Success(
                [new ProviderCapability(PaymentProvider.ECPay, true, true, true)]));
    }

    /// <summary>
    /// 只做這三條路要的事：取消、後台取消、短缺退款各自成功一次並記次數。
    /// 用 stub 而不是真的 <c>OrderingApplicationService</c>，是因為這裡要釘的是
    /// <b>BFF 那一層的控制流</b>——「副作用已經產生之後才失敗」——不是領域規則。
    /// </summary>
    private sealed class StubOrderingApplication(OrderView current) : IOrderingApplication
    {
        public OrderView Current { get; private set; } = current;

        public int CancelCustomerCalls { get; private set; }

        public int CancelAdminCalls { get; private set; }

        public int RefundShortfallCalls { get; private set; }

        public Task<Result<OrderView>> GetAdminAsync(
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(orderId == Current.Id
                ? Result<OrderView>.Success(Current)
                : Result<OrderView>.Failure("ordering.order-not-found", "找不到訂單。"));

        public Task<Result<OrderView>> CancelCustomerAsync(
            CustomerId customerId,
            OrderId orderId,
            string? reason,
            CancellationToken cancellationToken)
        {
            CancelCustomerCalls++;
            return Task.FromResult(Cancel());
        }

        public Task<Result<OrderView>> CancelAdminAsync(
            OrderId orderId,
            string reason,
            RefundDestination refundTo,
            CancellationToken cancellationToken)
        {
            CancelAdminCalls++;
            return Task.FromResult(Cancel());
        }

        public Task<Result<OrderView>> RefundLineShortfallAsync(
            OrderId orderId,
            OrderLineId lineId,
            string reason,
            RefundDestination refundTo,
            CancellationToken cancellationToken)
        {
            RefundShortfallCalls++;
            Current = Current with
            {
                Lines = Current.Lines.Select(candidate => candidate.Id == lineId
                    ? candidate with { RefundedAmount = candidate.UnitPrice }
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
        public Task<Result<OrderPage<OrderView>>> ListAdminAsync(
            AdminOrderListRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OrderView>> CancelLineAsync(
            OrderId orderId,
            OrderLineId lineId,
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

        private Result<OrderView> Cancel()
        {
            Current = Current with
            {
                Status = OrderStatus.Cancelled,
                Lines = Current.Lines
                    .Select(line => line with { Status = OrderLineStatus.Unavailable })
                    .ToArray(),
            };
            return Current;
        }
    }
}
