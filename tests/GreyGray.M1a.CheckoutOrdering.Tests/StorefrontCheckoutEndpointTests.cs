using GreyGray.Api.Storefront;
using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Checkout.Core;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;
using StorefrontEndpoints = GreyGray.Api.Storefront.M1aEndpoints;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

/// <summary>
/// BE-34：把「現在卡在哪 #22」（checkout 幽靈訂單／冪等鍵被 abandon）從推論變成證據。
/// BE-35 修好之後，這一組測試從「記錄缺陷」轉成「守住修法」——同一個觸發條件
/// （第 ③ 步 catalog 讀不到）現在要回 201、冪等鍵 <c>Completed</c>、回應用退化值。
/// </summary>
/// <remarks>
/// <para>
/// <c>POST /v1/cart/checkout</c> 在一個 HTTP 請求裡串了三個<b>各自獨立交易</b>的步驟：
/// ① <c>checkout.CompleteAsync</c>（購物車結案，自己 SaveChanges）→
/// ② <c>ordering.CreateFromCheckoutAsync</c>（訂單自己 SaveChanges，此時訂單已落庫）→
/// ③ <c>ToOrderAsync</c>（讀 catalog／customers 組回應）。中間沒有任何補償，而
/// <c>BffHttp.ExecuteIdempotentAsync</c> 只要拿到 <c>Result.Failure</c> 或例外就
/// <c>AbandonAsync</c>，完全不看副作用是不是已經 commit。
/// </para>
/// <para>
/// 這一組測試刻意用<b>真的</b> <see cref="CheckoutApplicationService"/> 與
/// <see cref="OrderingApplicationService"/>（不是行為自己寫死的 stub），兩者共用同一個
/// Pricing 與 Catalog，但各自持有<b>獨立的</b> <see cref="FakeUnitOfWork"/>——因為
/// 「兩段各自 commit」正是這個問題的核心，用同一個工作單元會把它掩蓋掉。
/// 只有 <see cref="ISessionStore"/>／<see cref="IIdempotencyStore"/>／
/// <see cref="ICatalogQuery"/> 是測試替身。
/// </para>
/// </remarks>
public sealed class StorefrontCheckoutEndpointTests
{
    private const string CheckoutScope = "storefront:cart:checkout";
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 3, 0, 0, TimeSpan.Zero);

    // ── 情境 1／(A′) 幽靈訂單：BE-35 修好之後的樣子 ──────────────────────────

    [Fact(DisplayName = "BE-35 情境1：第 ③ 步 catalog 失敗時仍回 201，回應用退化值，冪等鍵 Completed")]
    public async Task Catalog_failure_while_rendering_degrades_the_response_instead_of_failing_checkout()
    {
        var fixture = new CheckoutEndpointFixture();
        await fixture.SeedCartLineAsync();
        fixture.BreakCatalogAfterCheckoutCompletes();

        var (status, body) = await fixture.CheckoutAsync("key-1");

        // 客人看到的：201。訂單已經 commit，組回應失敗不該讓它看起來像沒發生。
        status.ShouldBe(StatusCodes.Status201Created);
        body.ShouldNotContain("ordering.catalog-snapshot-unavailable");

        // 資料庫實際發生的：訂單真的建立了，而且付款也已經被要求。
        var orders = await fixture.ListOrdersAsync();
        orders.Count.ShouldBe(1, "第 ② 步已經 SaveChanges，訂單是真的存在的。");
        fixture.OrderingPublisher.Published.OfType<OrderPlaced>().Count().ShouldBe(1);
        fixture.OrderingPublisher.Published.OfType<PaymentRequested>().Count().ShouldBe(1);

        // 冪等鍵：Completed。副作用已經產生就不准 abandon。
        fixture.Idempotency.StatusOf("key-1", CheckoutScope)
            .ShouldBe(InspectableIdempotencyStore.EntryStatus.Completed);

        // 退化回應仍符合契約：Order.lines[].name／productId 都是必填，不可以是 null 或空字串。
        // 商品名讀不到就用 SkuId 當顯示名；productId 退成全零 ID，仍是 32 字元十六進位，
        // 符合契約 Id 的 pattern。
        body.ShouldContain($"\"name\":\"{fixture.Sku.Id}\"");
        body.ShouldContain("\"productId\":\"00000000000000000000000000000000\"");

        // 不准靜默：每一次退化都要留下可觀測痕跡。
        fixture.Logger.Entries.ShouldContain(
            entry => entry.Level == LogLevel.Error && entry.Message.Contains("讀不到 SKU"),
            "退化不留痕就是把故障藏起來。");
    }

    [Fact(DisplayName = "BE-34 情境2：同一把 key 重試不會建出第二張訂單，回的是原本那一張")]
    public async Task Retry_with_the_same_key_replays_the_first_order_instead_of_creating_a_second()
    {
        var fixture = new CheckoutEndpointFixture();
        await fixture.SeedCartLineAsync();
        fixture.BreakCatalogAfterCheckoutCompletes();

        var first = await fixture.CheckoutAsync("key-1");
        first.Status.ShouldBe(
            StatusCodes.Status201Created,
            "BE-35 之後第一次就回 201（退化回應），不再是 422。");
        var ghost = (await fixture.ListOrdersAsync()).ShouldHaveSingleItem();

        // catalog 恢復（模擬暫時性故障結束），同一把 key、同一個 CartId 重送。
        fixture.HealCatalog();
        var (status, body) = await fixture.CheckoutAsync("key-1");

        status.ShouldBe(StatusCodes.Status201Created);
        var orders = await fixture.ListOrdersAsync();
        orders.Count.ShouldBe(
            1,
            "★ 這就是要釘死的東西：不會建出第二張訂單。BE-35 之後連業務邏輯都不會重跑——" +
            "冪等鍵是 Completed，第二次直接回快取。");
        orders[0].Id.ShouldBe(ghost.Id);
        body.ShouldContain(ghost.OrderNumber!);
        fixture.Ordering.CreateCalls.ShouldBe(
            1,
            "副作用已 commit 就不 abandon，所以重送不會再進 work 一次。");
        fixture.Idempotency.StatusOf("key-1", CheckoutScope)
            .ShouldBe(InspectableIdempotencyStore.EntryStatus.Completed);
    }

    [Fact(DisplayName = "BE-35 情境3：結帳成功後換一把 key 重送會被購物車狀態擋下，訂單完好")]
    public async Task Retry_with_a_different_key_is_refused_but_the_order_stays_intact()
    {
        var fixture = new CheckoutEndpointFixture();
        await fixture.SeedCartLineAsync();
        fixture.BreakCatalogAfterCheckoutCompletes();

        var first = await fixture.CheckoutAsync("key-1");
        first.Status.ShouldBe(
            StatusCodes.Status201Created,
            "BE-35 之後 (A″) 的前提不成立了：客人第一次就拿到 201，沒有理由換 key 重送。");
        var ghost = (await fixture.ListOrdersAsync()).ShouldHaveSingleItem();

        fixture.HealCatalog();
        var (status, body) = await fixture.CheckoutAsync("key-2");

        // 第 ① 步就擋下來了：購物車已結案，而且不是同一把 key。這是正確行為，不是缺陷。
        status.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        body.ShouldContain("checkout.cart-already-completed");

        // 不會重複下單；work 在還沒產生副作用時失敗，abandon 是安全的。
        (await fixture.ListOrdersAsync()).Count.ShouldBe(1);
        fixture.Idempotency.StatusOf("key-2", CheckoutScope)
            .ShouldBe(InspectableIdempotencyStore.EntryStatus.Abandoned);

        // 訂單本身沒有壞：客人在「我的訂單」還是看得到它。
        var visible = await fixture.Ordering.GetCustomerAsync(
            fixture.Customer.Id,
            ghost.Id,
            TestContext.Current.CancellationToken);
        visible.IsSuccess.ShouldBeTrue();
        visible.Value.Status.ShouldBe(OrderStatus.AwaitingPayment);
    }

    // ── 必做 3／(B) 購物車結案了但沒有訂單 ──────────────────────────────────

    [Fact(DisplayName = "BE-34 (B)：第 ② 步失敗時購物車已結案、訂單沒建立，冪等鍵一樣被 abandon")]
    public async Task Cart_is_completed_even_when_order_creation_fails()
    {
        var fixture = new CheckoutEndpointFixture();
        await fixture.SeedCartLineAsync();
        fixture.BreakPricingSnapshotRead();

        var (status, body) = await fixture.CheckoutAsync("key-1");

        status.ShouldBe(StatusCodes.Status404NotFound);
        body.ShouldContain("pricing.snapshot-not-found");
        (await fixture.ListOrdersAsync()).ShouldBeEmpty("第 ② 步失敗，訂單沒有建立。");
        fixture.Idempotency.StatusOf("key-1", CheckoutScope)
            .ShouldBe(InspectableIdempotencyStore.EntryStatus.Abandoned);

        // 第 ① 步已經 commit：購物車回不去了。
        var reuse = await fixture.AddAnotherLineAsync();
        reuse.IsFailure.ShouldBeTrue();
        reuse.Error.Code.ShouldBe(
            "checkout.cart-already-completed",
            "購物車在第 ① 步就已經結案並 SaveChanges，第 ② 步失敗不會把它退回去。");
    }

    [Fact(DisplayName = "BE-34 (B)：第 ② 步的失敗如果是暫時性的，同一把 key 重試會自動復原")]
    public async Task Same_key_retry_recovers_from_a_transient_step_two_failure()
    {
        var fixture = new CheckoutEndpointFixture();
        await fixture.SeedCartLineAsync();
        fixture.BreakPricingSnapshotRead();

        var first = await fixture.CheckoutAsync("key-1");
        first.Status.ShouldBe(StatusCodes.Status404NotFound);
        (await fixture.ListOrdersAsync()).ShouldBeEmpty();

        fixture.HealPricingSnapshotRead();
        var (status, _) = await fixture.CheckoutAsync("key-1");

        status.ShouldBe(StatusCodes.Status201Created);
        (await fixture.ListOrdersAsync()).Count.ShouldBe(
            1,
            "CompleteAsync 對同一把 key 會 ReplayCompletedEvent，所以重試會沿用同一個 " +
            "CheckoutCompleted 補建訂單，不會變成兩張。");
        fixture.Idempotency.StatusOf("key-1", CheckoutScope)
            .ShouldBe(InspectableIdempotencyStore.EntryStatus.Completed);
    }

    // ── 一般路徑的迴歸網（本來就綠，別因為今天綠就省略）────────────────────

    [Fact(DisplayName = "BE-34 迴歸網：一切正常時 checkout 回 201，冪等鍵 Completed，重送直接回快取")]
    public async Task Happy_path_completes_the_idempotency_key_and_replays_from_cache()
    {
        var fixture = new CheckoutEndpointFixture();
        await fixture.SeedCartLineAsync();

        var (status, body) = await fixture.CheckoutAsync("key-1");
        status.ShouldBe(StatusCodes.Status201Created);
        fixture.Idempotency.StatusOf("key-1", CheckoutScope)
            .ShouldBe(InspectableIdempotencyStore.EntryStatus.Completed);

        var (replayStatus, replayBody) = await fixture.CheckoutAsync("key-1");

        replayStatus.ShouldBe(StatusCodes.Status201Created);
        replayBody.ShouldBe(body, "AlreadyCompleted 要直接回快取，不重跑業務邏輯。");
        fixture.Ordering.CreateCalls.ShouldBe(
            1,
            "第二次請求連 action lambda 都不該進去。");
        (await fixture.ListOrdersAsync()).Count.ShouldBe(1);
    }

    // ── 缺陷的迴歸測試：確認修好之後應該長什麼樣 ──────────────────────────

    [Fact(DisplayName = "BE-34 缺陷(A′)：訂單已經建立時，checkout 不得回錯誤、也不得 abandon 冪等鍵")]
    public async Task Committed_order_must_not_be_reported_as_a_failure()
    {
        var fixture = new CheckoutEndpointFixture();
        await fixture.SeedCartLineAsync();
        fixture.BreakCatalogAfterCheckoutCompletes();

        var (status, _) = await fixture.CheckoutAsync("key-1");

        // 這兩條是任何一種修法都必須滿足的不變式，刻意不綁定特定做法：
        // 訂單既然已經 commit，這個操作在邏輯上就已經完成了。
        var orders = await fixture.ListOrdersAsync();
        orders.Count.ShouldBe(1);
        status.ShouldBe(
            StatusCodes.Status201Created,
            "第 ③ 步只是組回應，它失敗不該讓已經成立的下單看起來像沒發生。");
        fixture.Idempotency.StatusOf("key-1", CheckoutScope).ShouldBe(
            InspectableIdempotencyStore.EntryStatus.Completed,
            "副作用已 commit 就不可以 AbandonAsync——那等於把冪等保護解除。");
    }

    [Fact(
        DisplayName = "BE-34 缺陷(B)：checkout 回失敗時，購物車不得停留在「已結案但沒有訂單」",
        Skip = "缺陷 (B)，明文不在 BE-35 範圍（docs/31 §2）：第 ① 步已 commit、第 ② 步才失敗，" +
               "要根治得把 checkout saga 化（BE-34 選項 5），會動到契約與前端。" +
               "現況有兩層緩解：同一把 key 重試會自癒，且 CheckoutCompleted 與購物車結案同一個" +
               "交易寫進 outbox，Worker 會非同步補建訂單。")]
    public async Task Failed_checkout_must_not_leave_a_completed_cart_without_an_order()
    {
        var fixture = new CheckoutEndpointFixture();
        await fixture.SeedCartLineAsync();
        fixture.BreakPricingSnapshotRead();

        var (status, _) = await fixture.CheckoutAsync("key-1");
        status.ShouldNotBe(StatusCodes.Status201Created);

        // 不變式：購物車結案與訂單存在必須同進同出。要嘛補償把購物車退回可編輯，
        // 要嘛訂單真的建立起來——不可以兩者不一致。
        var orderCount = (await fixture.ListOrdersAsync()).Count;
        if (orderCount == 0)
        {
            var reuse = await fixture.AddAnotherLineAsync();
            reuse.IsSuccess.ShouldBeTrue(
                "訂單沒建立，購物車就該退回可編輯狀態，客人才回得去結帳。");
        }
    }

    // ── 測試替身與 fixture ─────────────────────────────────────────────────

    /// <summary>
    /// 真的 Checkout ＋ 真的 Ordering，共用 Pricing／Catalog，但各自持有獨立的工作單元。
    /// </summary>
    private sealed class CheckoutEndpointFixture
    {
        private readonly FakeCampaignQuery _campaigns;
        private readonly GatedPricing _pricing;
        private readonly CheckoutApplicationService _realCheckout;
        private readonly string _sessionToken;

        public CheckoutEndpointFixture()
        {
            Sku = new SkuSnapshot(
                SkuId.New(),
                ProductId.New(),
                "面膜",
                "10 入",
                300,
                new Dimensions(20, 15, 5),
                true)
            {
                ListPrice = new Money(12_000, Currency.TWD),
            };
            Offer = new CampaignOffer(
                CampaignOfferId.New(),
                CampaignId.New(),
                Sku.Id,
                new Money(10_000, Currency.TWD),
                null,
                true);
            Customer = new CustomerSummary(CustomerId.New(), "灰灰", MemberTier.Standard, true);
            Clock = new FakeClock(Now);
            Catalog = new ToggleableCatalogQuery(Sku);
            _campaigns = new FakeCampaignQuery(Offer);
            _pricing = new GatedPricing(new FakePricing(Clock));
            Customers = new FakeCustomerDirectory(Customer);
            CheckoutUnitOfWork = new FakeUnitOfWork();
            OrderingUnitOfWork = new FakeUnitOfWork();
            CheckoutPublisher = new FakeEventPublisher();
            OrderingPublisher = new FakeEventPublisher();
            _realCheckout = new CheckoutApplicationService(
                new FakeCartRepository(),
                CheckoutUnitOfWork,
                CheckoutPublisher,
                Catalog,
                _campaigns,
                new FakeInventoryQuery(Sku.Id, 10),
                _pricing,
                new FakePaymentQuery(),
                Customers,
                Clock,
                new FakeCorrelation());
            Checkout = _realCheckout;
            Ordering = new CountingOrderingApplication(new OrderingApplicationService(
                new FakeOrderRepository(),
                OrderingUnitOfWork,
                OrderingPublisher,
                _pricing,
                Clock,
                new FakeCorrelation()));
            Sessions = new FakeSessionStore();
            _sessionToken = Sessions.Issue(Customer.Id);
            Idempotency = new InspectableIdempotencyStore();
            Logger = new CapturingLogger();
        }

        public CartId CartId { get; } = CartId.New();

        public SkuSnapshot Sku { get; }

        public CampaignOffer Offer { get; }

        public CustomerSummary Customer { get; }

        public FakeClock Clock { get; }

        public ToggleableCatalogQuery Catalog { get; }

        public FakeCustomerDirectory Customers { get; }

        public FakeUnitOfWork CheckoutUnitOfWork { get; }

        public FakeUnitOfWork OrderingUnitOfWork { get; }

        public FakeEventPublisher CheckoutPublisher { get; }

        public FakeEventPublisher OrderingPublisher { get; }

        public ICheckoutApplication Checkout { get; private set; }

        public CountingOrderingApplication Ordering { get; }

        public FakeSessionStore Sessions { get; }

        public InspectableIdempotencyStore Idempotency { get; }

        /// <summary>BE-35：退化回應必須留痕，這裡收下所有 log 供斷言。</summary>
        public CapturingLogger Logger { get; }

        /// <summary>把一條 Preorder 品項放進購物車，讓 checkout 有東西可以結。</summary>
        public async Task SeedCartLineAsync()
        {
            var added = await _realCheckout.AddLineAsync(
                new AddCartLineRequest(
                    CartId,
                    Customer.Id,
                    Sku.Id,
                    FulfillmentMode.Preorder,
                    Offer.Id,
                    1),
                TestContext.Current.CancellationToken);
            added.IsSuccess.ShouldBeTrue($"seed 失敗就沒有測試前提：{added.Error.Code}");
        }

        /// <summary>再加一條品項——用來觀察購物車是不是已經結案（結案後會被擋下）。</summary>
        public Task<Result<CartView>> AddAnotherLineAsync() =>
            _realCheckout.AddLineAsync(
                new AddCartLineRequest(
                    CartId,
                    Customer.Id,
                    Sku.Id,
                    FulfillmentMode.Preorder,
                    Offer.Id,
                    1),
                TestContext.Current.CancellationToken);

        /// <summary>
        /// 讓第 ③ 步（<c>ToOrderAsync</c>）必定失敗，而第 ①② 步照常跑完。
        /// 用裝飾器在 <c>CompleteAsync</c> 成功回傳<b>之後</b>才把 catalog 切成失敗，
        /// 這樣 catalog 仍然是<b>同一個實例</b>（跟正式環境的 DI 一樣），時序卻是確定的。
        /// </summary>
        public void BreakCatalogAfterCheckoutCompletes() =>
            Checkout = new CatalogBreaksAfterCheckout(_realCheckout, Catalog);

        public void HealCatalog()
        {
            Catalog.Fails = false;
            Checkout = _realCheckout;
        }

        /// <summary>
        /// 讓第 ② 步失敗。挑的是<b>真實會發生</b>的失敗原因：Ordering 建單時要回頭跟
        /// Pricing 拿凍結好的報價快照（<c>GetSnapshotAsync</c>），那是一次跨模組讀取。
        /// </summary>
        public void BreakPricingSnapshotRead() => _pricing.SnapshotReadFails = true;

        public void HealPricingSnapshotRead() => _pricing.SnapshotReadFails = false;

        public async Task<(int Status, string Body)> CheckoutAsync(string idempotencyKey)
        {
            var context = BuildContext(idempotencyKey);
            var result = await StorefrontEndpoints.CompleteCheckoutAsync(
                new StorefrontEndpoints.CompleteCheckoutInput(
                    DeliveryMethod.ConvenienceStore,
                    ShippingPolicy.HoldUntilComplete,
                    null,
                    "991234",
                    null),
                context,
                Sessions,
                Checkout,
                Ordering,
                Catalog,
                Customers,
                Idempotency,
                Logger,
                TestContext.Current.CancellationToken);
            await result.ExecuteAsync(context);
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body);
            var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            return (context.Response.StatusCode, body);
        }

        public async Task<IReadOnlyList<OrderView>> ListOrdersAsync()
        {
            var page = await Ordering.ListCustomerAsync(
                new CustomerOrderListRequest(Customer.Id, null, null),
                TestContext.Current.CancellationToken);
            page.IsSuccess.ShouldBeTrue();
            return page.Value.Items;
        }

        private DefaultHttpContext BuildContext(string idempotencyKey)
        {
            var context = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            };
            context.Request.Headers["Idempotency-Key"] = idempotencyKey;
            context.Request.Headers.Cookie = $"gg_session={_sessionToken}; gg_cart={CartId}";
            context.Response.Body = new MemoryStream();
            return context;
        }
    }

    /// <summary>可以隨時切成失敗的 catalog；正式環境 checkout 與 BFF 共用同一個實例。</summary>
    private sealed class ToggleableCatalogQuery(SkuSnapshot sku) : ICatalogQuery
    {
        public bool Fails { get; set; }

        public Task<Result<SkuSnapshot>> GetSkuAsync(
            SkuId id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Fails
                ? Result<SkuSnapshot>.Failure("catalog.unavailable", "商品資料暫時讀不到。")
                : id == sku.Id
                    ? Result<SkuSnapshot>.Success(sku)
                    : Result<SkuSnapshot>.Failure("catalog.sku-not-found", "找不到 SKU。"));

        public Task<Result<IReadOnlyList<SkuSnapshot>>> GetSkusAsync(
            IReadOnlyCollection<SkuId> ids,
            CancellationToken cancellationToken) =>
            Task.FromResult(Fails
                ? Result<IReadOnlyList<SkuSnapshot>>.Failure("catalog.unavailable", "商品資料暫時讀不到。")
                : ids.All(id => id == sku.Id)
                    ? Result<IReadOnlyList<SkuSnapshot>>.Success([sku])
                    : Result<IReadOnlyList<SkuSnapshot>>.Failure("catalog.sku-not-found", "找不到 SKU。"));
    }

    /// <summary>
    /// 真的執行第 ① 步，成功之後才把 catalog 切成失敗——用來把「結帳完成之後 catalog
    /// 才讀不到」這個時序做成確定性的。<b>不改變第 ① 步本身的任何行為。</b>
    /// </summary>
    private sealed class CatalogBreaksAfterCheckout(
        ICheckoutApplication inner,
        ToggleableCatalogQuery catalog) : ICheckoutApplication
    {
        public async Task<Result<CheckoutCompleted>> CompleteAsync(
            CompleteCheckoutRequest request,
            CancellationToken cancellationToken)
        {
            var completed = await inner.CompleteAsync(request, cancellationToken);
            if (completed.IsSuccess)
            {
                catalog.Fails = true;
            }

            return completed;
        }

        public Task<Result<CartView>> GetCartAsync(
            CartId cartId,
            CustomerId? customerId,
            CancellationToken cancellationToken) =>
            inner.GetCartAsync(cartId, customerId, cancellationToken);

        public Task<Result<CartView>> AddLineAsync(
            AddCartLineRequest request,
            CancellationToken cancellationToken) =>
            inner.AddLineAsync(request, cancellationToken);

        public Task<Result<CartView>> UpdateLineAsync(
            UpdateCartLineRequest request,
            CancellationToken cancellationToken) =>
            inner.UpdateLineAsync(request, cancellationToken);

        public Task<Result<CartView>> RemoveLineAsync(
            RemoveCartLineRequest request,
            CancellationToken cancellationToken) =>
            inner.RemoveLineAsync(request, cancellationToken);

        public Task<Result<CheckoutQuote>> QuoteAsync(
            QuoteCartRequest request,
            CancellationToken cancellationToken) =>
            inner.QuoteAsync(request, cancellationToken);
    }

    /// <summary>
    /// 包住 <see cref="FakePricing"/>：<c>QuoteAsync</c>／<c>FreezeAsync</c>（Checkout 用）
    /// 照常，只有 <c>GetSnapshotAsync</c>（Ordering 建單時用）可以切成失敗。
    /// </summary>
    private sealed class GatedPricing(FakePricing inner) : IPricingQuotation
    {
        public bool SnapshotReadFails { get; set; }

        public Task<Result<PricingSnapshot>> QuoteAsync(
            QuoteRequest request,
            CancellationToken cancellationToken) =>
            inner.QuoteAsync(request, cancellationToken);

        public Task<Result<PricingSnapshotId>> FreezeAsync(
            PricingSnapshot snapshot,
            CancellationToken cancellationToken) =>
            inner.FreezeAsync(snapshot, cancellationToken);

        public Task<Result<PricingSnapshot>> GetSnapshotAsync(
            PricingSnapshotId id,
            CancellationToken cancellationToken) =>
            SnapshotReadFails
                ? Task.FromResult(Result<PricingSnapshot>.Failure(
                    "pricing.snapshot-not-found",
                    "找不到報價快照。"))
                : inner.GetSnapshotAsync(id, cancellationToken);
    }

    /// <summary>只多數一件事：<c>CreateFromCheckoutAsync</c> 被呼叫了幾次。</summary>
    internal sealed class CountingOrderingApplication(IOrderingApplication inner)
        : IOrderingApplication
    {
        public int CreateCalls { get; private set; }

        public Task<Result<OrderView>> CreateFromCheckoutAsync(
            CheckoutCompleted checkout,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            return inner.CreateFromCheckoutAsync(checkout, cancellationToken);
        }

        public Task<Result<OrderPage<OrderView>>> ListCustomerAsync(
            CustomerOrderListRequest request,
            CancellationToken cancellationToken) =>
            inner.ListCustomerAsync(request, cancellationToken);

        public Task<Result<OrderView>> GetCustomerAsync(
            CustomerId customerId,
            OrderId orderId,
            CancellationToken cancellationToken) =>
            inner.GetCustomerAsync(customerId, orderId, cancellationToken);

        public Task<Result<OrderView>> CancelCustomerAsync(
            CustomerId customerId,
            OrderId orderId,
            string? reason,
            CancellationToken cancellationToken) =>
            inner.CancelCustomerAsync(customerId, orderId, reason, cancellationToken);

        public Task<Result<OrderPage<OrderView>>> ListAdminAsync(
            AdminOrderListRequest request,
            CancellationToken cancellationToken) =>
            inner.ListAdminAsync(request, cancellationToken);

        public Task<Result<OrderView>> GetAdminAsync(
            OrderId orderId,
            CancellationToken cancellationToken) =>
            inner.GetAdminAsync(orderId, cancellationToken);

        public Task<Result<OrderView>> CancelAdminAsync(
            OrderId orderId,
            string reason,
            RefundDestination refundTo,
            CancellationToken cancellationToken) =>
            inner.CancelAdminAsync(orderId, reason, refundTo, cancellationToken);

        public Task<Result<OrderView>> CancelLineAsync(
            OrderId orderId,
            OrderLineId lineId,
            string reason,
            RefundDestination refundTo,
            CancellationToken cancellationToken) =>
            inner.CancelLineAsync(orderId, lineId, reason, refundTo, cancellationToken);

        public Task<Result<OrderView>> RefundLineShortfallAsync(
            OrderId orderId,
            OrderLineId lineId,
            string reason,
            RefundDestination refundTo,
            CancellationToken cancellationToken) =>
            inner.RefundLineShortfallAsync(orderId, lineId, reason, refundTo, cancellationToken);

        public Task<Result> RecordPaymentCapturedAsync(
            OrderId orderId,
            Money amount,
            CancellationToken cancellationToken) =>
            inner.RecordPaymentCapturedAsync(orderId, amount, cancellationToken);

        public Task<Result> RecordPaymentFailedAsync(
            OrderId orderId,
            string failureCode,
            CancellationToken cancellationToken) =>
            inner.RecordPaymentFailedAsync(orderId, failureCode, cancellationToken);

        public Task<Result> RecordPaymentRefundedAsync(
            OrderId orderId,
            Money amount,
            CancellationToken cancellationToken) =>
            inner.RecordPaymentRefundedAsync(orderId, amount, cancellationToken);

        public Task<Result> RecordItemPurchasedAsync(
            OrderLineId orderLineId,
            int quantityPurchased,
            CancellationToken cancellationToken) =>
            inner.RecordItemPurchasedAsync(orderLineId, quantityPurchased, cancellationToken);
    }

    /// <summary>記憶體 session；只需要能發一張 Customer session 並讀回來。</summary>
    internal sealed class FakeSessionStore : ISessionStore
    {
        private readonly Dictionary<string, SessionRecord> _sessions = [];

        public string Issue(CustomerId customerId)
        {
            var token = Guid.CreateVersion7().ToString("N");
            _sessions[token] = new SessionRecord(
                customerId.ToString(),
                SessionSubjectKind.Customer,
                TenantId.Default,
                null,
                Now,
                Now.AddDays(30));
            return token;
        }

        public Task<string> CreateAsync(
            string subjectId,
            SessionSubjectKind subjectKind,
            TenantId tenantId,
            string? role,
            CancellationToken cancellationToken)
        {
            var token = Guid.CreateVersion7().ToString("N");
            _sessions[token] = new SessionRecord(
                subjectId,
                subjectKind,
                tenantId,
                role,
                Now,
                Now.AddDays(30));
            return Task.FromResult(token);
        }

        public Task<SessionRecord?> GetAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult(_sessions.TryGetValue(token, out var session) ? session : null);

        public Task DeleteAsync(string token, CancellationToken cancellationToken)
        {
            _sessions.Remove(token);
            return Task.CompletedTask;
        }
    }
}
