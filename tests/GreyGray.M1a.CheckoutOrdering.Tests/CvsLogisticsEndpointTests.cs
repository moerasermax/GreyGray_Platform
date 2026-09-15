using GreyGray.Api.Storefront;
using GreyGray.Api.Storefront.Logistics;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;
using GreyGray.Tools.EcpaySimulator.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

public sealed class CvsLogisticsEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 4, 0, 0, TimeSpan.Zero);
    private const string MerchantId = "DEVFAKE0000";
    private const string SelectionId = "AbCdEf0123456789GhIj";

    [Fact(DisplayName = "沒物流設定不擋組態；開地圖回 503、回傳則 303 not-configured")]
    public async Task Missing_settings_are_fail_open_at_startup_and_explicit_at_the_endpoints()
    {
        var configuration = Configuration();
        CvsLogisticsEndpoints.ReadSettings(configuration).ShouldBeNull();
        var cache = new InspectableDistributedCache();
        var clock = new FakeClock(Now);

        var mapContext = Context();
        var mapResult = await CvsLogisticsEndpoints.CreateMapSessionAsync(
            new CvsMapSessionInput(null),
            mapContext,
            configuration,
            null,
            cache,
            clock,
            TestContext.Current.CancellationToken);
        await mapResult.ExecuteAsync(mapContext);
        mapContext.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);

        var replyContext = Context();
        await SetFormAsync(replyContext, ValidReply());
        var reply = await CvsLogisticsEndpoints.HandleMapReplyAsync(
            replyContext,
            configuration,
            null,
            cache,
            clock,
            NullLogger.Instance,
            TestContext.Current.CancellationToken);
        await reply.ExecuteAsync(replyContext);
        AssertRedirect(replyContext, "cvsSelectionError=not-configured");
    }

    [Theory(DisplayName = "MerchantId 有值時，MapUrl 與 LogisticsSubType 的壞設定立即炸且點名鍵")]
    [InlineData(null, "UNIMARTC2C", false, "Logistics:ECPay:MapUrl")]
    [InlineData("/Express/map", "UNIMARTC2C", false, "Logistics:ECPay:MapUrl")]
    [InlineData("http://logistics.ecpay.com.tw/Express/map", "UNIMARTC2C", false, "Logistics:ECPay:MapUrl")]
    [InlineData("https://ecpay.com.tw.evil.test/Express/map", "UNIMARTC2C", false, "Logistics:ECPay:MapUrl")]
    [InlineData("https://logistics.ecpay.com.tw/Express/map", "FAMIC2C", false, "Logistics:ECPay:LogisticsSubType")]
    public void Configured_but_invalid_settings_fail_fast(
        string? mapUrl,
        string subtype,
        bool allowNonEcpay,
        string expectedKey)
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            CvsLogisticsEndpoints.ReadSettings(Configuration(
                merchantId: MerchantId,
                mapUrl: mapUrl,
                subtype: subtype,
                allowNonEcpay: allowNonEcpay)));

        exception.Message.ShouldContain(expectedKey);
    }

    [Fact(DisplayName = "壞物流設定從真的 M1a 掛路由入口就會炸")]
    public async Task Invalid_settings_fail_from_the_real_route_mapping_entry()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logistics:ECPay:MerchantId"] = MerchantId,
            ["Logistics:ECPay:MapUrl"] = "not-a-url",
        });
        await using var app = builder.Build();

        var exception = Should.Throw<InvalidOperationException>(() =>
            app.MapM1aStorefrontEndpoints());

        exception.Message.ShouldContain("Logistics:ECPay:MapUrl");
    }

    [Fact(DisplayName = "開票產生 20 位英數票、SHA-256 快取鍵、欄位順序、Device 與 no-store")]
    public async Task Creating_map_sessions_returns_the_exact_form_shape_and_independent_tickets()
    {
        var configuration = Configuration(
            merchantId: MerchantId,
            mapUrl: "http://127.0.0.1:5009/Express/map",
            allowNonEcpay: true,
            publicApiOrigin: "https://api.greygray.shop/");
        var settings = CvsLogisticsEndpoints.ReadSettings(configuration)!;
        var cache = new InspectableDistributedCache();
        var clock = new FakeClock(Now);
        var context = Context();

        var firstResult = await CvsLogisticsEndpoints.CreateMapSessionAsync(
            new CvsMapSessionInput(CvsMapDevice.Mobile),
            context,
            configuration,
            settings,
            cache,
            clock,
            TestContext.Current.CancellationToken);
        var first = ((IValueHttpResult)firstResult).Value.ShouldBeOfType<CvsMapSessionResponse>();
        var secondResult = await CvsLogisticsEndpoints.CreateMapSessionAsync(
            new CvsMapSessionInput(null),
            context,
            configuration,
            settings,
            cache,
            clock,
            TestContext.Current.CancellationToken);
        var second = ((IValueHttpResult)secondResult).Value.ShouldBeOfType<CvsMapSessionResponse>();

        CvsLogisticsEndpoints.IsValidSelectionId(first.SelectionId).ShouldBeTrue();
        first.SelectionId.ShouldNotBe(second.SelectionId);
        first.ExpiresAt.ShouldBe(Now.AddMinutes(15));
        first.Method.ShouldBe("POST");
        first.Action.ShouldBe(settings.MapUrl);
        first.Fields.Keys.ShouldBe([
            "MerchantID", "LogisticsType", "LogisticsSubType", "IsCollection",
            "ServerReplyURL", "ExtraData", "Device",
        ]);
        first.Fields["ServerReplyURL"].ShouldBe(
            "https://api.greygray.shop/v1/logistics/cvs-map/reply");
        first.Fields["ExtraData"].ShouldBe(first.SelectionId);
        first.Fields["Device"].ShouldBe("1");
        second.Fields.ShouldNotContainKey("Device");
        context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
        context.Response.Headers.SetCookie.ToString().ShouldContain("gg_cart=");
        CvsLogisticsEndpoints.CacheKey(first.SelectionId).ShouldStartWith(
            CvsLogisticsEndpoints.SelectionCacheKeyPrefix);
        CvsLogisticsEndpoints.CacheKey(first.SelectionId).ShouldNotContain(first.SelectionId);
        (await CvsLogisticsEndpoints.GetTicketAsync(
            cache,
            first.SelectionId,
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        (await CvsLogisticsEndpoints.GetTicketAsync(
            cache,
            second.SelectionId,
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    [Fact(DisplayName = "模擬器組出的回傳餵進真的處理函式會 303、選成門市，且全程不靠 cookie")]
    public async Task Simulator_reply_is_accepted_by_the_real_handler_without_a_cookie()
    {
        var cache = new InspectableDistributedCache();
        var clock = new FakeClock(Now);
        var cartId = CartId.New();
        await SeedPendingAsync(cache, SelectionId, cartId, Now.AddMinutes(15));
        var mapForm = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MerchantID"] = MerchantId,
            ["LogisticsType"] = "CVS",
            ["LogisticsSubType"] = "UNIMARTC2C",
            ["IsCollection"] = "N",
            ["ServerReplyURL"] = "https://api.greygray.shop/v1/logistics/cvs-map/reply",
            ["ExtraData"] = SelectionId,
        };
        var replyFields = EcpaySimulatorCore.BuildCvsMapReplyFields(
            mapForm,
            EcpaySimulatorCore.FakeStores.Single(store => store.IsOutlying));
        var context = Context();
        await SetFormAsync(context, replyFields);

        var result = await HandleReplyAsync(context, cache, clock);
        await result.ExecuteAsync(context);

        AssertRedirect(context, $"cvsSelection={SelectionId}");
        context.Response.Headers.ContainsKey("Set-Cookie").ShouldBeFalse();
        var selected = (await CvsLogisticsEndpoints.GetTicketAsync(
            cache,
            SelectionId,
            TestContext.Current.CancellationToken))!;
        selected.Status.ShouldBe(CvsSelectionStatus.Selected);
        selected.StoreCode.ShouldBe("991003");
        selected.IsOutlying.ShouldBeTrue();
        selected.ExpiresAt.ShouldBe(Now.AddMinutes(60));
    }

    [Fact(DisplayName = "偽造 MerchantID 不燒票；真的回傳仍成功；再送一次才 already-used")]
    public async Task Forged_reply_keeps_the_ticket_pending_and_a_second_valid_reply_is_already_used()
    {
        var cache = new InspectableDistributedCache();
        var clock = new FakeClock(Now);
        var cartId = CartId.New();
        await SeedPendingAsync(cache, SelectionId, cartId, Now.AddMinutes(15));

        var forgedContext = Context();
        var forged = ValidReply();
        forged["MerchantID"] = "FORGED";
        await SetFormAsync(forgedContext, forged);
        var forgedResult = await HandleReplyAsync(forgedContext, cache, clock);
        await forgedResult.ExecuteAsync(forgedContext);
        AssertRedirect(forgedContext, "cvsSelectionError=invalid-reply");
        (await CvsLogisticsEndpoints.GetTicketAsync(
            cache,
            SelectionId,
            TestContext.Current.CancellationToken))!.Status.ShouldBe(CvsSelectionStatus.Pending);

        var validContext = Context();
        await SetFormAsync(validContext, ValidReply());
        var validResult = await HandleReplyAsync(validContext, cache, clock);
        await validResult.ExecuteAsync(validContext);
        AssertRedirect(validContext, $"cvsSelection={SelectionId}");

        var repeatedContext = Context();
        await SetFormAsync(repeatedContext, ValidReply());
        var repeatedResult = await HandleReplyAsync(repeatedContext, cache, clock);
        await repeatedResult.ExecuteAsync(repeatedContext);
        AssertRedirect(repeatedContext, "cvsSelectionError=already-used");
    }

    [Fact(DisplayName = "門市欄位上限可接受；超長、非英數與控制字元被拒且票仍 Pending")]
    public async Task Store_field_boundaries_are_enforced_without_burning_the_ticket()
    {
        var clock = new FakeClock(Now);
        var acceptedCache = new InspectableDistributedCache();
        const string acceptedId = "00000000000000000001";
        await SeedPendingAsync(acceptedCache, acceptedId, CartId.New(), Now.AddMinutes(15));
        var acceptedFields = ValidReply(acceptedId);
        acceptedFields["CVSStoreID"] = new string('A', 20);
        acceptedFields["CVSStoreName"] = new string('名', 50);
        acceptedFields["CVSAddress"] = new string('址', 200);
        var acceptedContext = Context();
        await SetFormAsync(acceptedContext, acceptedFields);
        var accepted = await HandleReplyAsync(acceptedContext, acceptedCache, clock);
        await accepted.ExecuteAsync(acceptedContext);
        AssertRedirect(acceptedContext, $"cvsSelection={acceptedId}");

        var invalidValues = new Action<Dictionary<string, string>>[]
        {
            fields => fields["CVSStoreID"] = new string('A', 21),
            fields => fields["CVSStoreID"] = "123-456",
            fields => fields["CVSStoreName"] = new string('名', 51),
            fields => fields["CVSAddress"] = new string('址', 201),
            fields => fields["CVSStoreName"] = "門市\n名稱",
            fields => fields["CVSAddress"] = "地址\u0000值",
        };
        for (var index = 0; index < invalidValues.Length; index++)
        {
            var selectionId = (index + 10).ToString("D20", System.Globalization.CultureInfo.InvariantCulture);
            var cache = new InspectableDistributedCache();
            await SeedPendingAsync(cache, selectionId, CartId.New(), Now.AddMinutes(15));
            var fields = ValidReply(selectionId);
            invalidValues[index](fields);
            var context = Context();
            await SetFormAsync(context, fields);

            var result = await HandleReplyAsync(context, cache, clock);
            await result.ExecuteAsync(context);

            AssertRedirect(context, "cvsSelectionError=invalid-reply");
            (await CvsLogisticsEndpoints.GetTicketAsync(
                cache,
                selectionId,
                TestContext.Current.CancellationToken))!.Status.ShouldBe(CvsSelectionStatus.Pending);
        }
    }

    [Theory(DisplayName = "選店票格式只接受恰好 20 個 ASCII 英數")]
    [InlineData("1234567890123456789", false)]
    [InlineData("12345678901234567890", true)]
    [InlineData("123456789012345678901", false)]
    [InlineData("1234567890123456789-", false)]
    [InlineData("                    ", false)]
    public void Selection_id_format_is_shared_and_strict(string selectionId, bool expected) =>
        CvsLogisticsEndpoints.IsValidSelectionId(selectionId).ShouldBe(expected);

    [Fact(DisplayName = "Pending 第 15 分鐘整點、Selected 第 60 分鐘整點都已過期")]
    public async Task Ticket_expiry_is_inclusive_at_both_boundaries()
    {
        var clock = new FakeClock(Now);
        var pendingCache = new InspectableDistributedCache();
        await SeedPendingAsync(pendingCache, SelectionId, CartId.New(), Now);
        var pendingContext = Context();
        await SetFormAsync(pendingContext, ValidReply());
        var pendingResult = await HandleReplyAsync(pendingContext, pendingCache, clock);
        await pendingResult.ExecuteAsync(pendingContext);
        AssertRedirect(pendingContext, "cvsSelectionError=expired");

        var selectedCache = new InspectableDistributedCache();
        var cartId = CartId.New();
        await SeedSelectedAsync(selectedCache, SelectionId, cartId, Now);
        var selectedContext = Context(cartId);
        var selectedResult = await CvsLogisticsEndpoints.GetSelectionAsync(
            SelectionId,
            selectedContext,
            selectedCache,
            clock,
            TestContext.Current.CancellationToken);
        await selectedResult.ExecuteAsync(selectedContext);
        selectedContext.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact(DisplayName = "讀票只給同一台購物車；沒 cookie 不發新 cookie；格式錯不查快取")]
    public async Task Reading_selection_does_not_leak_or_create_cart_cookies()
    {
        var cache = new InspectableDistributedCache();
        var ownerCart = CartId.New();
        await SeedSelectedAsync(cache, SelectionId, ownerCart, Now.AddMinutes(60));
        var clock = new FakeClock(Now);

        var strangerContext = Context(CartId.New());
        var strangerResult = await CvsLogisticsEndpoints.GetSelectionAsync(
            SelectionId, strangerContext, cache, clock, TestContext.Current.CancellationToken);
        await strangerResult.ExecuteAsync(strangerContext);
        strangerContext.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);

        var noCookieContext = Context();
        var noCookieResult = await CvsLogisticsEndpoints.GetSelectionAsync(
            SelectionId, noCookieContext, cache, clock, TestContext.Current.CancellationToken);
        await noCookieResult.ExecuteAsync(noCookieContext);
        noCookieContext.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        noCookieContext.Response.Headers.ContainsKey("Set-Cookie").ShouldBeFalse();

        var callsBefore = cache.GetCalls;
        var badFormatContext = Context(ownerCart);
        var badFormatResult = await CvsLogisticsEndpoints.GetSelectionAsync(
            "bad-ticket", badFormatContext, cache, clock, TestContext.Current.CancellationToken);
        await badFormatResult.ExecuteAsync(badFormatContext);
        badFormatContext.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        cache.GetCalls.ShouldBe(callsBefore);

        var ownerContext = Context(ownerCart);
        var ownerResult = await CvsLogisticsEndpoints.GetSelectionAsync(
            SelectionId, ownerContext, cache, clock, TestContext.Current.CancellationToken);
        ((IValueHttpResult)ownerResult).Value.ShouldBeOfType<CvsStoreSelectionResponse>();
        ownerContext.Response.Headers.CacheControl.ToString().ShouldBe("private, no-store");
    }

    [Fact(DisplayName = "回傳端點遇到快取讀寫失敗都 303 unavailable，不洩漏 500")]
    public async Task Reply_cache_failures_redirect_to_unavailable()
    {
        var clock = new FakeClock(Now);
        var readFailure = new InspectableDistributedCache { ThrowOnGet = true };
        var readContext = Context();
        await SetFormAsync(readContext, ValidReply());
        var readResult = await HandleReplyAsync(readContext, readFailure, clock);
        await readResult.ExecuteAsync(readContext);
        AssertRedirect(readContext, "cvsSelectionError=unavailable");

        var writeFailure = new InspectableDistributedCache();
        await SeedPendingAsync(writeFailure, SelectionId, CartId.New(), Now.AddMinutes(15));
        writeFailure.ThrowOnSet = true;
        var writeContext = Context();
        await SetFormAsync(writeContext, ValidReply());
        var writeResult = await HandleReplyAsync(writeContext, writeFailure, clock);
        await writeResult.ExecuteAsync(writeContext);
        AssertRedirect(writeContext, "cvsSelectionError=unavailable");
    }

    [Fact(DisplayName = "非 form 與壞 form 都 303 invalid-reply；RequestAborted 取消不被吞掉")]
    public async Task Form_parse_failures_are_redirected_but_request_cancellation_is_rethrown()
    {
        var cache = new InspectableDistributedCache();
        var clock = new FakeClock(Now);

        var nonForm = Context();
        var nonFormResult = await HandleReplyAsync(nonForm, cache, clock);
        await nonFormResult.ExecuteAsync(nonForm);
        AssertRedirect(nonForm, "cvsSelectionError=invalid-reply");

        var malformed = Context();
        malformed.Features.Set<IFormFeature>(new ThrowingFormFeature(
            new InvalidDataException("bad form")));
        var malformedResult = await HandleReplyAsync(malformed, cache, clock);
        await malformedResult.ExecuteAsync(malformed);
        AssertRedirect(malformed, "cvsSelectionError=invalid-reply");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = Context();
        cancelled.RequestAborted = cts.Token;
        cancelled.Features.Set<IFormFeature>(new ThrowingFormFeature(
            new OperationCanceledException(cts.Token)));
        await Should.ThrowAsync<OperationCanceledException>(() =>
            HandleReplyAsync(cancelled, cache, clock));
    }

    [Fact(DisplayName = "Admin 訂單詳情原值帶出門市代號、名稱與地址，不做舊資料退回")]
    public async Task Admin_order_response_carries_all_three_store_snapshot_fields()
    {
        var customer = new CustomerSummary(CustomerId.New(), "灰灰", MemberTier.Standard, true);
        var order = new OrderView(
            OrderId.New(),
            customer.Id,
            SourceChannel.Own,
            OrderStatus.AwaitingPayment,
            ShippingPolicy.HoldUntilComplete,
            PricingSnapshotId.New(),
            Money.Zero(Currency.TWD),
            Money.Zero(Currency.TWD),
            Money.Zero(Currency.TWD),
            [],
            Now)
        {
            DeliveryMethod = DeliveryMethod.ConvenienceStore,
            ConvenienceStoreCode = "991001",
            ConvenienceStoreName = "模擬門市（dev）",
            ConvenienceStoreAddress = "台北市模擬路 1 號",
        };
        var sku = new SkuSnapshot(
            SkuId.New(),
            ProductId.New(),
            "測試",
            null,
            0,
            new Dimensions(0, 0, 0),
            true);

        var response = await GreyGray.Api.Admin.M1aEndpoints.ToAdminOrderAsync(
            order,
            new FakeCustomerDirectory(customer),
            new FakePaymentQuery(),
            new FakeCatalogQuery(sku),
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        response.ConvenienceStoreCode.ShouldBe("991001");
        response.ConvenienceStoreName.ShouldBe("模擬門市（dev）");
        response.ConvenienceStoreAddress.ShouldBe("台北市模擬路 1 號");
    }

    private static IConfiguration Configuration(
        string? merchantId = null,
        string? mapUrl = null,
        string? subtype = null,
        bool allowNonEcpay = false,
        string publicOrigin = "https://shop.greygray.test",
        string? publicApiOrigin = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logistics:ECPay:MerchantId"] = merchantId,
                ["Logistics:ECPay:MapUrl"] = mapUrl,
                ["Logistics:ECPay:LogisticsSubType"] = subtype,
                ["Logistics:ECPay:AllowNonEcpayEndpoints"] = allowNonEcpay.ToString(),
                ["Storefront:PublicOrigin"] = publicOrigin,
                ["Storefront:PublicApiOrigin"] = publicApiOrigin,
            })
            .Build();

    private static DefaultHttpContext Context(CartId? cartId = null)
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
        if (cartId is not null)
        {
            context.Request.Headers.Cookie = $"gg_cart={cartId.Value}";
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task SetFormAsync(
        HttpContext context,
        IReadOnlyDictionary<string, string> fields)
    {
        using var content = new FormUrlEncodedContent(fields);
        context.Request.ContentType = content.Headers.ContentType!.ToString();
        context.Request.Body = new MemoryStream(
            await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    private static Dictionary<string, string> ValidReply(string selectionId = SelectionId) =>
        new(StringComparer.Ordinal)
        {
            ["MerchantID"] = MerchantId,
            ["MerchantTradeNo"] = string.Empty,
            ["LogisticsSubType"] = "UNIMARTC2C",
            ["CVSStoreID"] = "991001",
            ["CVSStoreName"] = "模擬門市（dev）",
            ["CVSAddress"] = "台北市模擬路 1 號",
            ["CVSTelephone"] = "02-20000001",
            ["CVSOutSide"] = "0",
            ["ExtraData"] = selectionId,
        };

    private static Task SeedPendingAsync(
        IDistributedCache cache,
        string selectionId,
        CartId cartId,
        DateTimeOffset expiresAt) =>
        CvsLogisticsEndpoints.SetTicketAsync(
            cache,
            selectionId,
            new CvsSelectionTicket(
                cartId.Value,
                CvsSelectionStatus.Pending,
                Now,
                expiresAt,
                null,
                null,
                null,
                false),
            TestContext.Current.CancellationToken);

    private static Task SeedSelectedAsync(
        IDistributedCache cache,
        string selectionId,
        CartId cartId,
        DateTimeOffset expiresAt) =>
        CvsLogisticsEndpoints.SetTicketAsync(
            cache,
            selectionId,
            new CvsSelectionTicket(
                cartId.Value,
                CvsSelectionStatus.Selected,
                Now,
                expiresAt,
                "991001",
                "模擬門市（dev）",
                "台北市模擬路 1 號",
                false),
            TestContext.Current.CancellationToken);

    private static async Task<IResult> HandleReplyAsync(
        HttpContext context,
        IDistributedCache cache,
        IClock clock) =>
        await CvsLogisticsEndpoints.HandleMapReplyAsync(
            context,
            Configuration(
                MerchantId,
                "http://127.0.0.1:5009/Express/map",
                allowNonEcpay: true),
            new CvsMapSettings(
                MerchantId,
                "UNIMARTC2C",
                new Uri("http://127.0.0.1:5009/Express/map")),
            cache,
            clock,
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

    private static void AssertRedirect(HttpContext context, string expectedQuery)
    {
        context.Response.StatusCode.ShouldBe(StatusCodes.Status303SeeOther);
        context.Response.Headers.Location.ToString().ShouldContain(expectedQuery);
    }

    private sealed class ThrowingFormFeature(Exception exception) : IFormFeature
    {
        public bool HasFormContentType => true;

        public IFormCollection? Form { get; set; }

        public IFormCollection ReadForm() => throw exception;

        public Task<IFormCollection> ReadFormAsync(CancellationToken cancellationToken) =>
            Task.FromException<IFormCollection>(exception);
    }
}
