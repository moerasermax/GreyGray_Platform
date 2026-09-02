using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Checkout.Core;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;
using Shouldly;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;
using PricingDeliveryMethod = GreyGray.Modules.Pricing.Contracts.DeliveryMethod;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

/// <summary>
/// ADR-030（#37）：<c>shippingPolicy</c> 只有混合購物車才必填，單一模式由後端推導。
/// </summary>
/// <remarks>
/// <para>
/// 使用者 2026-09-02 親自走旅程，購物車只放一件預購商品就結帳 500——
/// 契約把 <c>shippingPolicy</c> 寫成一律必填，前端照 description 只在混合時問，
/// 送出 <c>null</c> 之後在 <b>request body 綁定期</b>就丟 <c>JsonException</c>，
/// 比登入檢查還早，客人連 401 都拿不到。
/// </para>
/// <para>
/// 規則的主人是後端：<c>hasMixedModes</c> 本來就是 Checkout 算的。所以這裡驗兩層——
/// 純函式 <c>ResolveShippingPolicy</c>（好窮舉），以及真的 <see cref="CheckoutApplicationService"/>
/// 走完 <c>CompleteAsync</c> 之後 <see cref="CheckoutCompleted.ShippingPolicy"/> 到底是什麼
/// （<c>CheckoutCompleted</c> 與 <c>Order</c> 的這個欄位<b>維持不可為 null</b>，Ordering 一行不動）。
/// </para>
/// </remarks>
public sealed class ShippingPolicyResolutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);

    // ── 純函式：把四種組合窮舉完 ──────────────────────────────────────────

    [Fact(DisplayName = "純現貨沒帶值 → 推導成 ShipSeparately")]
    public void Stock_only_without_a_choice_resolves_to_ship_separately()
    {
        var resolved = CheckoutApplicationService.ResolveShippingPolicy(
            [FulfillmentMode.Stock, FulfillmentMode.Stock],
            null);

        resolved.IsSuccess.ShouldBeTrue();
        resolved.Value.ShouldBe(ShippingPolicy.ShipSeparately);
    }

    [Fact(DisplayName = "純預購沒帶值 → 推導成 HoldUntilComplete")]
    public void Preorder_only_without_a_choice_resolves_to_hold_until_complete()
    {
        var resolved = CheckoutApplicationService.ResolveShippingPolicy(
            [FulfillmentMode.Preorder],
            null);

        resolved.IsSuccess.ShouldBeTrue();
        resolved.Value.ShouldBe(ShippingPolicy.HoldUntilComplete);
    }

    [Fact(DisplayName = "單一模式帶了值 → 忽略，不報錯（舊客戶端仍會送）")]
    public void Single_mode_ignores_the_value_the_client_sent()
    {
        CheckoutApplicationService
            .ResolveShippingPolicy([FulfillmentMode.Stock], ShippingPolicy.HoldUntilComplete)
            .Value.ShouldBe(ShippingPolicy.ShipSeparately);
        CheckoutApplicationService
            .ResolveShippingPolicy([FulfillmentMode.Preorder], ShippingPolicy.ShipSeparately)
            .Value.ShouldBe(ShippingPolicy.HoldUntilComplete);
    }

    [Fact(DisplayName = "混合沒帶值 → checkout.shipping-policy-required")]
    public void Mixed_without_a_choice_fails_with_the_dedicated_code()
    {
        var resolved = CheckoutApplicationService.ResolveShippingPolicy(
            [FulfillmentMode.Stock, FulfillmentMode.Preorder],
            null);

        resolved.IsFailure.ShouldBeTrue();
        resolved.Error.Code.ShouldBe("checkout.shipping-policy-required");
    }

    [Fact(DisplayName = "混合帶了值 → 用客人的值")]
    public void Mixed_with_a_choice_uses_it()
    {
        CheckoutApplicationService
            .ResolveShippingPolicy(
                [FulfillmentMode.Stock, FulfillmentMode.Preorder],
                ShippingPolicy.ShipSeparately)
            .Value.ShouldBe(ShippingPolicy.ShipSeparately);
        CheckoutApplicationService
            .ResolveShippingPolicy(
                [FulfillmentMode.Preorder, FulfillmentMode.Stock],
                ShippingPolicy.HoldUntilComplete)
            .Value.ShouldBe(ShippingPolicy.HoldUntilComplete);
    }

    // ── 真的走一次 CompleteAsync ──────────────────────────────────────────

    [Fact(DisplayName = "純預購結帳不帶 shippingPolicy → 成立，事件帶 HoldUntilComplete")]
    public async Task Preorder_only_checkout_succeeds_without_a_shipping_policy()
    {
        var fixture = new Fixture();
        await fixture.AddLineAsync(FulfillmentMode.Preorder);

        var completed = await fixture.CompleteAsync(null);

        completed.IsSuccess.ShouldBeTrue(
            completed.IsFailure ? completed.Error.Code : string.Empty);
        completed.Value.ShippingPolicy.ShouldBe(ShippingPolicy.HoldUntilComplete);
    }

    [Fact(DisplayName = "純現貨結帳不帶 shippingPolicy → 成立，事件帶 ShipSeparately")]
    public async Task Stock_only_checkout_succeeds_without_a_shipping_policy()
    {
        var fixture = new Fixture();
        await fixture.AddLineAsync(FulfillmentMode.Stock);

        var completed = await fixture.CompleteAsync(null);

        completed.IsSuccess.ShouldBeTrue(
            completed.IsFailure ? completed.Error.Code : string.Empty);
        completed.Value.ShippingPolicy.ShouldBe(ShippingPolicy.ShipSeparately);
    }

    [Fact(DisplayName = "混合購物車不帶 shippingPolicy → checkout.shipping-policy-required，不發事件")]
    public async Task Mixed_checkout_without_a_shipping_policy_is_rejected()
    {
        var fixture = new Fixture();
        await fixture.AddLineAsync(FulfillmentMode.Stock);
        await fixture.AddLineAsync(FulfillmentMode.Preorder);
        fixture.Publisher.Reset();

        var completed = await fixture.CompleteAsync(null);

        completed.IsFailure.ShouldBeTrue();
        completed.Error.Code.ShouldBe("checkout.shipping-policy-required");
        fixture.Publisher.Published.ShouldBeEmpty("被擋下來就不該有 CheckoutCompleted。");
    }

    [Fact(DisplayName = "混合購物車帶了 shippingPolicy → 用客人選的那個")]
    public async Task Mixed_checkout_keeps_the_customers_choice()
    {
        var fixture = new Fixture();
        await fixture.AddLineAsync(FulfillmentMode.Stock);
        await fixture.AddLineAsync(FulfillmentMode.Preorder);

        var completed = await fixture.CompleteAsync(ShippingPolicy.ShipSeparately);

        completed.IsSuccess.ShouldBeTrue(
            completed.IsFailure ? completed.Error.Code : string.Empty);
        completed.Value.ShippingPolicy.ShouldBe(ShippingPolicy.ShipSeparately);
    }

    /// <summary>真的 <see cref="CheckoutApplicationService"/>，只有周邊是替身。</summary>
    private sealed class Fixture
    {
        public Fixture()
        {
            Sku = new SkuSnapshot(
                SkuId.New(),
                ProductId.New(),
                "雪花秀潤燥精華",
                "60 ml",
                300,
                new Dimensions(20, 15, 5),
                true)
            {
                ListPrice = new Money(120_000, Currency.TWD),
            };
            Offer = new CampaignOffer(
                CampaignOfferId.New(),
                CampaignId.New(),
                Sku.Id,
                new Money(100_000, Currency.TWD),
                null,
                true);
            Customer = new CustomerSummary(CustomerId.New(), "灰灰", MemberTier.Standard, true);
            var clock = new FakeClock(Now);
            Publisher = new FakeEventPublisher();
            Service = new CheckoutApplicationService(
                new FakeCartRepository(),
                new FakeUnitOfWork(),
                Publisher,
                new FakeCatalogQuery(Sku),
                new FakeCampaignQuery(Offer),
                new FakeInventoryQuery(Sku.Id, 10),
                new FakePricing(clock),
                new FakePaymentQuery(),
                new FakeCustomerDirectory(Customer),
                clock,
                new FakeCorrelation());
        }

        public CartId CartId { get; } = CartId.New();

        public SkuSnapshot Sku { get; }

        public CampaignOffer Offer { get; }

        public CustomerSummary Customer { get; }

        public FakeEventPublisher Publisher { get; }

        public CheckoutApplicationService Service { get; }

        /// <summary>
        /// 同一個 SKU 的 Stock 與 Preorder 是<b>兩條</b> line——
        /// <c>Cart.AddOrIncreaseLine</c> 連 <c>Mode</c> 一起比，所以一個 SKU 就能組出混合購物車。
        /// </summary>
        public async Task AddLineAsync(FulfillmentMode mode)
        {
            var added = await Service.AddLineAsync(
                new AddCartLineRequest(
                    CartId,
                    Customer.Id,
                    Sku.Id,
                    mode,
                    mode == FulfillmentMode.Preorder ? Offer.Id : null,
                    1),
                TestContext.Current.CancellationToken);
            added.IsSuccess.ShouldBeTrue(
                added.IsFailure ? $"seed 失敗就沒有測試前提：{added.Error.Code}" : string.Empty);
        }

        public Task<Result<CheckoutCompleted>> CompleteAsync(ShippingPolicy? shippingPolicy) =>
            Service.CompleteAsync(
                new CompleteCheckoutRequest(
                    CartId,
                    Customer.Id,
                    PricingDeliveryMethod.SelfPickup,
                    shippingPolicy,
                    null,
                    null,
                    null,
                    $"checkout-{Guid.CreateVersion7():N}"),
                TestContext.Current.CancellationToken);
    }
}
