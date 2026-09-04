using System.Text.Json;
using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Checkout.Core;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;
using PricingDeliveryMethod = GreyGray.Modules.Pricing.Contracts.DeliveryMethod;
using StorefrontEndpoints = GreyGray.Api.Storefront.M1aEndpoints;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

/// <summary>
/// #36：登出之後，訪客<b>永遠</b>加不進購物車。
/// </summary>
/// <remarks>
/// <para>
/// 購物車是綁在會員身上的（<c>Cart.IsAccessibleBy</c>：<c>CustomerId is null || CustomerId == customerId</c>）。
/// 登入狀態下建的車綁定該會員；登出只清了 <c>gg_session</c>，<c>gg_cart</c> 那顆 30 天的 cookie 留著，
/// 於是訪客拿著它做任何事都是 <c>checkout.cart-not-found</c>——toast 顯示「找不到購物車。」、
/// 購物車頁只剩「重試」、徽章停在舊數字，直到 cookie 過期或使用者自己清。
/// </para>
/// <para>
/// 兩層修法，缺一不可：①登出時把 <c>gg_cart</c> 一起刪掉；②就算 cookie 還在（別的分頁、
/// 手動貼上、舊瀏覽器），「看購物車」與「加入購物車」拿到 not-found 就換一顆新車，只重試一次。
/// <b>服務層的 not-found 語意刻意不動</b>——<c>/cart/quote</c>、<c>/cart/checkout</c> 仍要回 404，
/// 不洩漏「這顆車存在但不是你的」。
/// </para>
/// </remarks>
public sealed class StorefrontCartCookieTests
{
    private const string CartCookie = "gg_cart";
    private const string SessionCookie = "gg_session";
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "#36 登出同時讓 gg_cart 過期，不只清 gg_session")]
    public async Task Logout_expires_the_cart_cookie_too()
    {
        var fixture = new Fixture();
        var context = fixture.BuildContext(fixture.SessionToken, fixture.CartId);
        context.Request.Headers["Idempotency-Key"] = "logout-1";

        var result = await StorefrontEndpoints.LogoutAsync(
            context,
            fixture.Sessions,
            fixture.Idempotency,
            TestContext.Current.CancellationToken);
        await result.ExecuteAsync(context);

        var setCookies = context.Response.Headers.SetCookie.ToArray();
        setCookies.ShouldContain(
            cookie => cookie!.StartsWith($"{SessionCookie}=;", StringComparison.Ordinal),
            "登出本來就會清 session cookie。");
        var cartCookie = setCookies
            .Where(cookie => cookie!.StartsWith($"{CartCookie}=", StringComparison.Ordinal))
            .ToArray()
            .ShouldHaveSingleItem()
            .ShouldNotBeNull();
        cartCookie.ShouldStartWith($"{CartCookie}=;");
        cartCookie.ShouldContain("expires=Thu, 01 Jan 1970");

        // 刪除用的選項要跟 SetCartCookie 一致，否則瀏覽器不認為是同一顆，刪不掉。
        cartCookie.ShouldContain("path=/");
        cartCookie.ShouldContain("secure");
        cartCookie.ShouldContain("samesite=lax");
        cartCookie.ShouldContain("httponly");
    }

    [Fact(DisplayName = "#36 訪客拿著別人的 gg_cart 看購物車 → 200 空車＋換一顆新 cookie，不是 404")]
    public async Task Getting_someone_elses_cart_swaps_the_cookie_instead_of_404()
    {
        var fixture = new Fixture();
        await fixture.SeedCartOwnedByCustomerAsync();
        var context = fixture.BuildContext(sessionToken: null, fixture.CartId);

        var (status, body) = await fixture.ExecuteAsync(
            context,
            StorefrontEndpoints.GetCartAsync(
                context,
                fixture.Sessions,
                fixture.Service,
                TestContext.Current.CancellationToken));

        status.ShouldBe(StatusCodes.Status200OK, body);
        using var payload = JsonDocument.Parse(body);
        payload.RootElement.GetProperty("lines").GetArrayLength().ShouldBe(0);
        NewCartIdFrom(context).ShouldNotBe(fixture.CartId.ToString());
    }

    [Fact(DisplayName = "#36 訪客拿著別人的 gg_cart 加入商品 → 換新車重試成功，不是找不到購物車")]
    public async Task Adding_a_line_to_someone_elses_cart_swaps_the_cookie_and_succeeds()
    {
        var fixture = new Fixture();
        await fixture.SeedCartOwnedByCustomerAsync();
        var context = fixture.BuildContext(sessionToken: null, fixture.CartId);
        context.Request.Headers["Idempotency-Key"] = "add-line-1";

        var (status, body) = await fixture.ExecuteAsync(
            context,
            StorefrontEndpoints.AddCartLineAsync(
                new StorefrontEndpoints.AddCartLineInput(
                    fixture.Sku.Id,
                    FulfillmentMode.Stock,
                    null,
                    1),
                context,
                fixture.Sessions,
                fixture.Service,
                fixture.Idempotency,
                TestContext.Current.CancellationToken));

        status.ShouldBe(StatusCodes.Status200OK, body);
        using var payload = JsonDocument.Parse(body);
        var newCartId = payload.RootElement.GetProperty("id").GetString();
        newCartId.ShouldNotBe(fixture.CartId.ToString(), "換的是一顆全新的車。");
        payload.RootElement.GetProperty("lines").GetArrayLength().ShouldBe(1);
        NewCartIdFrom(context).ShouldBe(newCartId);
    }

    [Fact(DisplayName = "★ #44 看購物車拿到已結案的車 → 200 空車＋換一顆新 cookie，不是上一張單的品項")]
    public async Task Getting_a_completed_cart_swaps_the_cookie_and_returns_an_empty_cart()
    {
        var fixture = new Fixture();
        await fixture.SeedCartOwnedByCustomerAsync();
        await fixture.CompleteCartAsync();
        var context = fixture.BuildContext(fixture.SessionToken, fixture.CartId);

        var (status, body) = await fixture.ExecuteAsync(
            context,
            StorefrontEndpoints.GetCartAsync(
                context,
                fixture.Sessions,
                fixture.Service,
                TestContext.Current.CancellationToken));

        status.ShouldBe(StatusCodes.Status200OK, body);
        using var payload = JsonDocument.Parse(body);
        payload.RootElement.GetProperty("lines").GetArrayLength().ShouldBe(
            0,
            "不換車的話購物車頁與徽章會一直顯示上一張單的東西。");
        NewCartIdFrom(context).ShouldNotBe(fixture.CartId.ToString());
    }

    [Fact(DisplayName = "★ #44 CartView.IsCompleted 服務端真的有填——只加屬性不填就是型別有了沒人填")]
    public async Task Cart_view_reports_completion_so_the_host_can_see_it()
    {
        var fixture = new Fixture();
        await fixture.SeedCartOwnedByCustomerAsync();

        var before = await fixture.Service.GetCartAsync(
            fixture.CartId,
            fixture.Customer.Id,
            TestContext.Current.CancellationToken);
        before.IsSuccess.ShouldBeTrue();
        before.Value.IsCompleted.ShouldBeFalse();

        await fixture.CompleteCartAsync();

        var after = await fixture.Service.GetCartAsync(
            fixture.CartId,
            fixture.Customer.Id,
            TestContext.Current.CancellationToken);
        after.IsSuccess.ShouldBeTrue("服務層刻意不把「已結案」變成失敗，語意不動。");
        after.Value.IsCompleted.ShouldBeTrue("Host 只有 cookie，看不出這台車是不是下過單了。");
    }

    [Fact(DisplayName = "#36 服務層 not-found 語意不變：詢價與結帳拿別人的車仍是 cart-not-found")]
    public async Task Quote_and_checkout_still_report_not_found_for_someone_elses_cart()
    {
        var fixture = new Fixture();
        await fixture.SeedCartOwnedByCustomerAsync();
        var otherCustomer = CustomerId.New();

        var quoted = await fixture.Service.QuoteAsync(
            new QuoteCartRequest(fixture.CartId, otherCustomer, PricingDeliveryMethod.SelfPickup),
            TestContext.Current.CancellationToken);
        var completed = await fixture.Service.CompleteAsync(
            new CompleteCheckoutRequest(
                fixture.CartId,
                otherCustomer,
                PricingDeliveryMethod.SelfPickup,
                null,
                null,
                null,
                null,
                "checkout-not-mine"),
            TestContext.Current.CancellationToken);

        quoted.IsFailure.ShouldBeTrue();
        quoted.Error.Code.ShouldBe("checkout.cart-not-found");
        completed.IsFailure.ShouldBeTrue();
        completed.Error.Code.ShouldBe("checkout.cart-not-found");
    }

    /// <summary>回應裡新發的 <c>gg_cart</c>（沒有就是 null）。</summary>
    private static string? NewCartIdFrom(HttpContext context) => context.Response.Headers.SetCookie
        .Select(cookie => cookie ?? string.Empty)
        .Where(cookie => cookie.StartsWith($"{CartCookie}=", StringComparison.Ordinal))
        .Select(cookie => cookie[(CartCookie.Length + 1)..].Split(';')[0])
        .LastOrDefault();

    private sealed class Fixture
    {
        public Fixture()
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
            var clock = new FakeClock(Now);
            Service = new CheckoutApplicationService(
                new FakeCartRepository(),
                new FakeUnitOfWork(),
                new FakeEventPublisher(),
                new FakeCatalogQuery(Sku),
                new FakeCampaignQuery(Offer),
                new FakeInventoryQuery(Sku.Id, 10),
                new FakePricing(clock),
                new FakePaymentQuery(),
                new FakeCustomerDirectory(Customer),
                clock,
                new FakeCorrelation());
            Sessions = new StorefrontCheckoutEndpointTests.FakeSessionStore();
            SessionToken = Sessions.Issue(Customer.Id);
            Idempotency = new InspectableIdempotencyStore();
        }

        public CartId CartId { get; } = CartId.New();

        public SkuSnapshot Sku { get; }

        public CampaignOffer Offer { get; }

        public CustomerSummary Customer { get; }

        public CheckoutApplicationService Service { get; }

        public StorefrontCheckoutEndpointTests.FakeSessionStore Sessions { get; }

        public string SessionToken { get; }

        public InspectableIdempotencyStore Idempotency { get; }

        /// <summary>登入狀態下建一台車——登出之後這台車就不是訪客的了。</summary>
        public async Task SeedCartOwnedByCustomerAsync()
        {
            var added = await Service.AddLineAsync(
                new AddCartLineRequest(
                    CartId,
                    Customer.Id,
                    Sku.Id,
                    FulfillmentMode.Stock,
                    null,
                    1),
                TestContext.Current.CancellationToken);
            added.IsSuccess.ShouldBeTrue(
                added.IsFailure ? $"seed 失敗就沒有測試前提：{added.Error.Code}" : string.Empty);
        }

        /// <summary>把這台車結掉——#44 要的前提是「已經下過單」。</summary>
        public async Task CompleteCartAsync()
        {
            var completed = await Service.CompleteAsync(
                new CompleteCheckoutRequest(
                    CartId,
                    Customer.Id,
                    PricingDeliveryMethod.SelfPickup,
                    null,
                    null,
                    null,
                    null,
                    "checkout-done"),
                TestContext.Current.CancellationToken);
            completed.IsSuccess.ShouldBeTrue(
                completed.IsFailure
                    ? $"seed 失敗就沒有測試前提：{completed.Error.Code} {completed.Error.Message}"
                    : string.Empty);
        }

        public DefaultHttpContext BuildContext(string? sessionToken, CartId cartId)
        {
            var context = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            };
            context.Request.Headers.Cookie = sessionToken is null
                ? $"{CartCookie}={cartId}"
                : $"{SessionCookie}={sessionToken}; {CartCookie}={cartId}";
            context.Response.Body = new MemoryStream();
            return context;
        }

        public async Task<(int Status, string Body)> ExecuteAsync(
            DefaultHttpContext context,
            Task<IResult> pending)
        {
            var result = await pending;
            await result.ExecuteAsync(context);
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body);
            var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            return (context.Response.StatusCode, body);
        }
    }
}
