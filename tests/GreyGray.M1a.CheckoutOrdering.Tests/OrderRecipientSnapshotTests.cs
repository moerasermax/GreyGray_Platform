using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Checkout.Core;
using GreyGray.Modules.Checkout.Infra;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using AdminEndpoints = GreyGray.Api.Admin.M1aEndpoints;
using FulfillmentMode = GreyGray.Modules.Catalog.Contracts.FulfillmentMode;
using StorefrontEndpoints = GreyGray.Api.Storefront.M1aEndpoints;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

/// <summary>
/// BE-54 / ADR-039：收件人姓名與手機在下單當下凍結進訂單。
/// </summary>
/// <remarks>
/// <para>
/// 這一組要釘死的是 <b>#56</b>：訂單原本只存 <c>ShippingAddressId</c>，前台是<b>即時回查</b>
/// 地址簿，所以客人事後改地址、舊訂單跟著變，刪地址、舊訂單的收件資訊直接消失。
/// 測試刻意在下單<b>之後</b>去動地址簿，再看訂單——會變就是沒凍結。
/// </para>
/// <para>
/// 用真的 <see cref="CheckoutApplicationService"/> 與 <see cref="OrderingApplicationService"/>，
/// 兩者各自持有獨立的 <see cref="FakeUnitOfWork"/>（比照 <see cref="StorefrontCheckoutEndpointTests"/>：
/// 「兩段各自 commit」是這條路徑的事實，用同一個工作單元會把它掩蓋掉）。
/// </para>
/// </remarks>
public sealed class OrderRecipientSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 3, 0, 0, TimeSpan.Zero);

    // ── #56：宅配的收件人要從地址簿抄一份凍結 ───────────────────────────────

    [Fact(DisplayName = "ADR-039：宅配的收件人從地址簿抄，請求裡送來的值一律忽略")]
    public async Task Home_delivery_copies_the_recipient_from_the_address_book()
    {
        var fixture = new Fixture();
        await fixture.SeedCartLineAsync();
        fixture.Customers.AddressRecipientName = "王大明";
        fixture.Customers.AddressPhoneNumber = "0911222333";

        var result = await fixture.CheckoutAsync(
            "home-recipient",
            DeliveryMethod.HomeDelivery,
            shippingAddressId: AddressId.New(),
            // 客人（或惡意用戶端）送來的值：宅配時契約寫明會被忽略。
            recipientName: "駭客",
            recipientPhone: "0900000000");

        result.Status.ShouldBe(StatusCodes.Status201Created);
        var order = (await fixture.ListOrdersAsync()).ShouldHaveSingleItem();
        order.RecipientName.ShouldBe("王大明");
        order.RecipientPhone.ShouldBe("0911222333");
        // Leader 驗收後補：宅配地址也要凍結，而且是後台印得出來的單行字串
        // （郵遞區號 空格 縣市鄉鎮市區街道，欄位之間不加標點）。
        order.RecipientAddress.ShouldBe("100 臺北市中正區重慶南路一段122號");
        result.Body.ShouldContain("\"recipientName\":\"王大明\"");
        result.Body.ShouldContain("\"recipientPhone\":\"0911222333\"");
        // 前台 Order 契約<b>沒有</b> recipientAddress（只有 admin 的 AdminOrder 有）。
        result.Body.ShouldNotContain("recipientAddress");
    }

    [Fact(DisplayName = "ADR-039：超商取貨不填收件地址（門市資訊在 convenienceStore* 三欄）")]
    public async Task Convenience_store_orders_have_no_recipient_address()
    {
        var fixture = new Fixture();
        await fixture.SeedCartLineAsync();

        var result = await fixture.CheckoutAsync(
            "cvs-no-address",
            DeliveryMethod.ConvenienceStore,
            recipientName: "陳小美",
            recipientPhone: "0955666777");

        result.Status.ShouldBe(StatusCodes.Status201Created);
        (await fixture.ListOrdersAsync()).ShouldHaveSingleItem().RecipientAddress.ShouldBeNull();
    }

    [Fact(DisplayName = "#56 回歸：下單後改地址簿，舊訂單的收件人不變（前台回應也不變）")]
    public async Task Editing_the_address_book_does_not_change_an_existing_order()
    {
        var fixture = new Fixture();
        await fixture.SeedCartLineAsync();
        fixture.Customers.AddressRecipientName = "王大明";
        fixture.Customers.AddressPhoneNumber = "0911222333";

        var placed = await fixture.CheckoutAsync(
            "edit-address",
            DeliveryMethod.HomeDelivery,
            shippingAddressId: AddressId.New());
        placed.Status.ShouldBe(StatusCodes.Status201Created);
        var order = (await fixture.ListOrdersAsync()).ShouldHaveSingleItem();

        // 客人事後把地址改成另一個人、也改到別的縣市。
        fixture.Customers.AddressRecipientName = "李小華";
        fixture.Customers.AddressPhoneNumber = "0988777666";
        fixture.Customers.AddressPostalCode = "802";
        fixture.Customers.AddressCity = "高雄市";
        fixture.Customers.AddressDistrict = "苓雅區";
        fixture.Customers.AddressStreet = "四維三路 2 號";

        var reread = (await fixture.ListOrdersAsync()).ShouldHaveSingleItem();
        reread.RecipientName.ShouldBe("王大明", "訂單的收件人是下單當時凍結的事實，不是地址簿的現況。");
        reread.RecipientPhone.ShouldBe("0911222333");
        reread.RecipientAddress.ShouldBe(
            "100 臺北市中正區重慶南路一段122號",
            "地址跟姓名手機走同一條凍結路徑——會變就代表後台出貨會寄到新地址去。");

        // 前台回應：訂單層的收件人取快照，shippingAddress 區塊仍是即時回查（契約如此），
        // 所以同一份 JSON 裡兩個名字會不一樣——這正是「沒有從地址簿取」的證據。
        var body = await fixture.CancelOrderBodyAsync(order.Id, "cancel-key");
        body.ShouldContain("\"recipientName\":\"王大明\"");
        body.ShouldContain("\"recipientName\":\"李小華\"");
    }

    [Fact(DisplayName = "#56 回歸：下單後刪掉地址，舊訂單的收件人還在")]
    public async Task Deleting_the_address_keeps_the_frozen_recipient()
    {
        var fixture = new Fixture();
        await fixture.SeedCartLineAsync();
        fixture.Customers.AddressRecipientName = "王大明";
        fixture.Customers.AddressPhoneNumber = "0911222333";

        var placed = await fixture.CheckoutAsync(
            "delete-address",
            DeliveryMethod.HomeDelivery,
            shippingAddressId: AddressId.New());
        placed.Status.ShouldBe(StatusCodes.Status201Created);
        var order = (await fixture.ListOrdersAsync()).ShouldHaveSingleItem();

        fixture.Customers.AddressExists = false;

        var reread = (await fixture.ListOrdersAsync()).ShouldHaveSingleItem();
        reread.RecipientName.ShouldBe("王大明");
        reread.RecipientPhone.ShouldBe("0911222333");
        reread.RecipientAddress.ShouldBe("100 臺北市中正區重慶南路一段122號");

        var body = await fixture.CancelOrderBodyAsync(order.Id, "cancel-key");
        // shippingAddress 讀不到會退成 null（契約允許），但收件人是訂單自己的快照，照樣看得到。
        body.ShouldContain("\"shippingAddress\":null");
        body.ShouldContain("\"recipientName\":\"王大明\"");
        body.ShouldContain("\"recipientPhone\":\"0911222333\"");
    }

    // ── 超商取貨：客人自己填，缺了要回業務失敗而不是 500 ───────────────────

    [Theory(DisplayName = "ADR-039：超商取貨缺姓名或手機回 422 checkout.recipient-required")]
    [InlineData(null, "0912345678")]
    [InlineData("王小明", null)]
    [InlineData("   ", "0912345678")]
    [InlineData("王小明", "   ")]
    public async Task Convenience_store_requires_the_recipient(string? name, string? phone)
    {
        var fixture = new Fixture();
        await fixture.SeedCartLineAsync();

        var result = await fixture.CheckoutAsync(
            "cvs-missing-recipient",
            DeliveryMethod.ConvenienceStore,
            recipientName: name,
            recipientPhone: phone);

        result.Status.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        result.Body.ShouldContain("checkout.recipient-required");
        (await fixture.ListOrdersAsync()).ShouldBeEmpty();
    }

    [Fact(DisplayName = "ADR-039：收件人姓名或手機超長要在應用服務擋下，不是等 SaveChanges 炸 500")]
    public async Task Overlong_recipient_is_rejected_as_a_business_failure()
    {
        var tooLongName = new Fixture();
        await tooLongName.SeedCartLineAsync();
        var nameResult = await tooLongName.CheckoutAsync(
            "cvs-long-name",
            DeliveryMethod.ConvenienceStore,
            recipientName: new string('名', 51));

        nameResult.Status.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        nameResult.Body.ShouldContain("checkout.recipient-name-too-long");
        (await tooLongName.ListOrdersAsync()).ShouldBeEmpty();

        var tooLongPhone = new Fixture();
        await tooLongPhone.SeedCartLineAsync();
        var phoneResult = await tooLongPhone.CheckoutAsync(
            "cvs-long-phone",
            DeliveryMethod.ConvenienceStore,
            recipientPhone: new string('9', 21));

        phoneResult.Status.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        phoneResult.Body.ShouldContain("checkout.recipient-phone-too-long");
        (await tooLongPhone.ListOrdersAsync()).ShouldBeEmpty();
    }

    [Fact(DisplayName = "ADR-039：宅配地址組出來超過 200 字要回業務失敗，不截斷也不炸 500")]
    public async Task Overlong_home_delivery_address_is_rejected_instead_of_truncated()
    {
        var fixture = new Fixture();
        await fixture.SeedCartLineAsync();
        // Identity 的地址欄位在資料庫是 text（加密後存），沒有長度上限，
        // 所以組出來的單行字串理論上可能超過 varchar(200)。
        fixture.Customers.AddressStreet = new string('街', 200);

        var result = await fixture.CheckoutAsync(
            "home-long-address",
            DeliveryMethod.HomeDelivery,
            shippingAddressId: AddressId.New());

        result.Status.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        result.Body.ShouldContain("checkout.recipient-address-too-long");
        (await fixture.ListOrdersAsync()).ShouldBeEmpty(
            "被截斷的收件地址寄不到，而且沒有人會發現——寧可擋下來讓客人改地址。");
    }

    [Fact(DisplayName = "ADR-039：超商取貨凍結客人填的收件人，前後空白會被 trim")]
    public async Task Convenience_store_freezes_the_recipient_the_customer_typed()
    {
        var fixture = new Fixture();
        await fixture.SeedCartLineAsync();

        var result = await fixture.CheckoutAsync(
            "cvs-recipient",
            DeliveryMethod.ConvenienceStore,
            recipientName: "  陳小美  ",
            recipientPhone: " 0955666777 ");

        result.Status.ShouldBe(StatusCodes.Status201Created);
        var order = (await fixture.ListOrdersAsync()).ShouldHaveSingleItem();
        order.RecipientName.ShouldBe("陳小美");
        order.RecipientPhone.ShouldBe("0955666777");
    }

    // ── 事件重放：Cart.Complete 漏存就會在這裡變成 null ─────────────────────

    [Fact(DisplayName = "事件重放：outbox 重送 CheckoutCompleted 後訂單上的收件人快照不變")]
    public async Task Replayed_checkout_event_still_carries_the_frozen_recipient()
    {
        var fixture = new Fixture();
        await fixture.SeedCartLineAsync();
        fixture.Customers.AddressRecipientName = "王大明";
        fixture.Customers.AddressPhoneNumber = "0911222333";
        var addressId = AddressId.New();

        var placed = await fixture.CheckoutAsync(
            "replay-key",
            DeliveryMethod.HomeDelivery,
            shippingAddressId: addressId);
        placed.Status.ShouldBe(StatusCodes.Status201Created);
        var order = (await fixture.ListOrdersAsync()).ShouldHaveSingleItem();

        // 地址簿已經變了：若重放是「重新讀一次地址簿」而不是「重放凍結的事實」，這裡就會露餡。
        fixture.Customers.AddressRecipientName = "李小華";
        fixture.Customers.AddressPhoneNumber = "0988777666";

        // Worker 走的路：同一把 key 重進 CompleteAsync → Cart.ReplayCompletedEvent()。
        var replayed = await fixture.Checkout.CompleteAsync(
            fixture.BuildRequest(
                "replay-key",
                DeliveryMethod.HomeDelivery,
                addressId,
                null,
                null),
            TestContext.Current.CancellationToken);

        replayed.IsSuccess.ShouldBeTrue();
        replayed.Value.RecipientName.ShouldBe(
            "王大明",
            "★ Cart.Complete 的 Completed* 那一段漏了的話，重放出來的事件收件人會是 null。");
        replayed.Value.RecipientPhone.ShouldBe("0911222333");
        replayed.Value.RecipientAddress.ShouldBe("100 臺北市中正區重慶南路一段122號");

        // Ordering 對同一台購物車是冪等的：重送不會變成第二張訂單，也不會改掉快照。
        var recreated = await fixture.Ordering.CreateFromCheckoutAsync(
            replayed.Value,
            TestContext.Current.CancellationToken);
        recreated.IsSuccess.ShouldBeTrue();
        recreated.Value.Id.ShouldBe(order.Id);
        var after = (await fixture.ListOrdersAsync()).ShouldHaveSingleItem();
        after.RecipientName.ShouldBe("王大明");
        after.RecipientPhone.ShouldBe("0911222333");
        after.RecipientAddress.ShouldBe("100 臺北市中正區重慶南路一段122號");
    }

    // ── 冪等指紋 ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "ADR-039：同一把 Idempotency-Key 換掉收件人回 422 platform.idempotency-key-reused")]
    public async Task Same_idempotency_key_with_a_different_recipient_is_rejected()
    {
        var fixture = new Fixture();
        await fixture.SeedCartLineAsync();

        var first = await fixture.CheckoutAsync(
            "recipient-fingerprint",
            DeliveryMethod.ConvenienceStore,
            recipientName: "陳小美",
            recipientPhone: "0955666777");
        var second = await fixture.CheckoutAsync(
            "recipient-fingerprint",
            DeliveryMethod.ConvenienceStore,
            recipientName: "另一個人",
            recipientPhone: "0955666777");

        first.Status.ShouldBe(StatusCodes.Status201Created);
        // 加欄位之前這裡會直接回快取的舊回應、把新收件人靜靜吞掉。ADR-039 要的是這個改變。
        second.Status.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        second.Body.ShouldContain("platform.idempotency-key-reused");
        var orders = await fixture.ListOrdersAsync();
        orders.Count.ShouldBe(1);
        orders[0].RecipientName.ShouldBe("陳小美");
    }

    // ── 後台看得到明文 ─────────────────────────────────────────────────────

    [Fact(DisplayName = "ADR-039：後台訂單詳情回得出收件人明文，customerContactMasked 維持 null")]
    public async Task Admin_order_detail_exposes_the_recipient_in_plaintext()
    {
        var fixture = new Fixture();
        await fixture.SeedCartLineAsync();
        var placed = await fixture.CheckoutAsync(
            "admin-plaintext",
            DeliveryMethod.ConvenienceStore,
            recipientName: "陳小美",
            recipientPhone: "0955666777");
        placed.Status.ShouldBe(StatusCodes.Status201Created);
        var order = (await fixture.ListOrdersAsync()).ShouldHaveSingleItem();

        var admin = await AdminEndpoints.ToAdminOrderAsync(
            order,
            fixture.Customers,
            new FakePaymentQuery(),
            fixture.Catalog,
            new CapturingLogger(),
            TestContext.Current.CancellationToken);

        admin.RecipientName.ShouldBe("陳小美", "出貨的人要看得到明文，不遮罩、不加解鎖按鈕。");
        admin.RecipientPhone.ShouldBe("0955666777");
        admin.RecipientAddress.ShouldBeNull("超商取貨沒有收件地址，門市看 convenienceStore* 三欄。");
        admin.CustomerContactMasked.ShouldBeNull("契約保留這個欄位只為相容，恆為 null。");
    }

    [Fact(DisplayName = "ADR-039：後台宅配訂單看得到單行收件地址——沒有它就寄不出宅配")]
    public async Task Admin_home_delivery_order_shows_the_single_line_address()
    {
        var fixture = new Fixture();
        await fixture.SeedCartLineAsync();
        fixture.Customers.AddressRecipientName = "王大明";
        fixture.Customers.AddressPhoneNumber = "0911222333";

        var placed = await fixture.CheckoutAsync(
            "admin-home-address",
            DeliveryMethod.HomeDelivery,
            shippingAddressId: AddressId.New());
        placed.Status.ShouldBe(StatusCodes.Status201Created);
        var order = (await fixture.ListOrdersAsync()).ShouldHaveSingleItem();

        // 下單後客人把地址整筆刪掉：後台仍然要印得出當初那張單要寄去哪裡。
        fixture.Customers.AddressExists = false;

        var admin = await AdminEndpoints.ToAdminOrderAsync(
            order,
            fixture.Customers,
            new FakePaymentQuery(),
            fixture.Catalog,
            new CapturingLogger(),
            TestContext.Current.CancellationToken);

        admin.RecipientName.ShouldBe("王大明");
        admin.RecipientPhone.ShouldBe("0911222333");
        admin.RecipientAddress.ShouldBe("100 臺北市中正區重慶南路一段122號");
    }

    // ── 欄位對映：EF 的欄位名要跟 migration 0021 對得上 ─────────────────────

    [Fact(DisplayName = "0021：Cart 與 Order 的收件人都對映到 recipient_name／recipient_phone")]
    public void Recipient_columns_match_migration_0021()
    {
        using var checkout = new CheckoutDbContext(
            new DbContextOptionsBuilder<CheckoutDbContext>()
                .UseNpgsql("Host=127.0.0.1;Database=model;Username=model;Password=model")
                .Options);
        using var ordering = new OrderingDbContext(
            new DbContextOptionsBuilder<OrderingDbContext>()
                .UseNpgsql("Host=127.0.0.1;Database=model;Username=model;Password=model")
                .Options);

        var cart = checkout.Model.GetEntityTypes()
            .Single(entity => entity.GetTableName() == "cart");
        cart.GetProperty("CompletedRecipientName").GetColumnName().ShouldBe("recipient_name");
        cart.GetProperty("CompletedRecipientName").GetMaxLength().ShouldBe(50);
        cart.GetProperty("CompletedRecipientPhone").GetColumnName().ShouldBe("recipient_phone");
        cart.GetProperty("CompletedRecipientPhone").GetMaxLength().ShouldBe(20);
        cart.GetProperty("CompletedRecipientAddress").GetColumnName().ShouldBe("recipient_address");
        cart.GetProperty("CompletedRecipientAddress").GetMaxLength().ShouldBe(200);

        var orders = ordering.Model.GetEntityTypes()
            .Single(entity => entity.GetTableName() == "orders");
        orders.GetProperty("RecipientName").GetColumnName().ShouldBe("recipient_name");
        orders.GetProperty("RecipientName").GetMaxLength().ShouldBe(50);
        orders.GetProperty("RecipientPhone").GetColumnName().ShouldBe("recipient_phone");
        orders.GetProperty("RecipientPhone").GetMaxLength().ShouldBe(20);
        orders.GetProperty("RecipientAddress").GetColumnName().ShouldBe("recipient_address");
        orders.GetProperty("RecipientAddress").GetMaxLength().ShouldBe(200);
    }

    /// <summary>真 Checkout ＋ 真 Ordering，共用 Pricing／Catalog，各自持有獨立工作單元。</summary>
    private sealed class Fixture
    {
        private readonly CheckoutApplicationService _checkout;
        private readonly StorefrontCheckoutEndpointTests.FakeSessionStore _sessions;
        private readonly string _sessionToken;

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
            Clock = new FakeClock(Now);
            Catalog = new FakeCatalogQuery(Sku);
            Customers = new FakeCustomerDirectory(Customer)
            {
                // 單行格式的樣本：郵遞區號 空格 縣市鄉鎮市區街道，欄位之間不加標點。
                AddressPostalCode = "100",
                AddressCity = "臺北市",
                AddressDistrict = "中正區",
                AddressStreet = "重慶南路一段122號",
            };
            var pricing = new FakePricing(Clock);
            _checkout = new CheckoutApplicationService(
                new FakeCartRepository(),
                new FakeUnitOfWork(),
                new FakeEventPublisher(),
                Catalog,
                new FakeCampaignQuery(Offer),
                new FakeInventoryQuery(Sku.Id, 10),
                pricing,
                new FakePaymentQuery(),
                Customers,
                Clock,
                new FakeCorrelation());
            Ordering = new OrderingApplicationService(
                new FakeOrderRepository(),
                new FakeUnitOfWork(),
                new FakeEventPublisher(),
                pricing,
                Clock,
                new FakeCorrelation());
            _sessions = new StorefrontCheckoutEndpointTests.FakeSessionStore();
            _sessionToken = _sessions.Issue(Customer.Id);
            Idempotency = new InspectableIdempotencyStore();
            Cache = new InspectableDistributedCache();
            Logger = new CapturingLogger();
        }

        public CartId CartId { get; } = CartId.New();

        public SkuSnapshot Sku { get; }

        public CampaignOffer Offer { get; }

        public CustomerSummary Customer { get; }

        public FakeClock Clock { get; }

        public FakeCatalogQuery Catalog { get; }

        public FakeCustomerDirectory Customers { get; }

        public ICheckoutApplication Checkout => _checkout;

        public OrderingApplicationService Ordering { get; }

        public InspectableIdempotencyStore Idempotency { get; }

        public InspectableDistributedCache Cache { get; }

        public CapturingLogger Logger { get; }

        public async Task SeedCartLineAsync()
        {
            var added = await _checkout.AddLineAsync(
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

        public CompleteCheckoutRequest BuildRequest(
            string idempotencyKey,
            DeliveryMethod deliveryMethod,
            AddressId? shippingAddressId,
            string? recipientName,
            string? recipientPhone) =>
            new(
                CartId,
                Customer.Id,
                deliveryMethod,
                ShippingPolicy.HoldUntilComplete,
                shippingAddressId,
                deliveryMethod == DeliveryMethod.ConvenienceStore ? "991234" : null,
                null,
                idempotencyKey)
            {
                RecipientName = recipientName,
                RecipientPhone = recipientPhone,
            };

        public async Task<(int Status, string Body)> CheckoutAsync(
            string idempotencyKey,
            DeliveryMethod deliveryMethod,
            AddressId? shippingAddressId = null,
            string? recipientName = "王小明",
            string? recipientPhone = "0912345678")
        {
            var context = BuildContext(idempotencyKey);
            var result = await StorefrontEndpoints.CompleteCheckoutAsync(
                new StorefrontEndpoints.CompleteCheckoutInput(
                    deliveryMethod,
                    ShippingPolicy.HoldUntilComplete,
                    shippingAddressId,
                    null,
                    deliveryMethod == DeliveryMethod.ConvenienceStore ? "991234" : null,
                    recipientName,
                    recipientPhone,
                    null),
                context,
                _sessions,
                Checkout,
                Ordering,
                Catalog,
                Customers,
                Idempotency,
                Cache,
                Clock,
                Logger,
                TestContext.Current.CancellationToken);
            return await RenderAsync(result, context);
        }

        /// <summary>
        /// 取消訂單會用同一支 <c>ToOrderAsync</c> 組前台 <c>Order</c> 回應——用它把
        /// 「訂單層收件人取快照、shippingAddress 仍即時回查」這件事看出來。
        /// </summary>
        public async Task<string> CancelOrderBodyAsync(OrderId orderId, string idempotencyKey)
        {
            var context = BuildContext(idempotencyKey);
            var result = await StorefrontEndpoints.CancelCustomerOrderAsync(
                orderId.ToString(),
                new StorefrontEndpoints.CancelOrderInput("測試取消"),
                context,
                _sessions,
                Ordering,
                Catalog,
                Customers,
                Idempotency,
                Logger,
                TestContext.Current.CancellationToken);
            var (status, body) = await RenderAsync(result, context);
            status.ShouldBe(StatusCodes.Status200OK, body);
            return body;
        }

        public async Task<IReadOnlyList<OrderView>> ListOrdersAsync()
        {
            var page = await Ordering.ListCustomerAsync(
                new CustomerOrderListRequest(Customer.Id, null, null),
                TestContext.Current.CancellationToken);
            page.IsSuccess.ShouldBeTrue();
            return page.Value.Items;
        }

        private static async Task<(int Status, string Body)> RenderAsync(
            IResult result,
            DefaultHttpContext context)
        {
            await result.ExecuteAsync(context);
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body);
            var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            return (context.Response.StatusCode, body);
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
}
