using GreyGray.Api.Storefront;
using GreyGray.Modules.CustomerService.Contracts;
using GreyGray.Modules.CustomerService.Core;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Platform.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace GreyGray.CustomerService.Tests;

public sealed class StorefrontSupportEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "匿名留言成功建立工單，201 且不帶 customerId 以外的別人資料")]
    public async Task Anonymous_visitor_can_create_a_ticket()
    {
        var tickets = BuildService(out _);
        var context = Context();

        var result = await SupportEndpoints.CreateTicketAsync(
            new CreateSupportTicketInput("網站打不開", "a@b.com", null, ["常見問題", "網站問題"], null),
            context,
            new FakeSessionStore(),
            tickets,
            new InspectableIdempotencyStore(),
            new InspectableDistributedCache(),
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        var ticket = await ReadBodyAsync(result, context);
        ticket.Status.ShouldBe(SupportTicketStatus.Open);
        ticket.Message.ShouldBe("網站打不開");
        ticket.CustomerId.ShouldBeNull();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status201Created);
    }

    [Fact(DisplayName = "登入客人留言，工單帶得出 CustomerId")]
    public async Task Logged_in_customer_ticket_carries_customer_id()
    {
        var tickets = BuildService(out _);
        var sessions = new FakeSessionStore();
        var customerId = CustomerId.New();
        var token = sessions.Issue(SessionSubjectKind.Customer, customerId.ToString());
        var context = Context();
        context.Request.Headers.Cookie = $"gg_session={token}";

        var result = await SupportEndpoints.CreateTicketAsync(
            new CreateSupportTicketInput("我的訂單狀態", null, "0912345678", [], null),
            context,
            sessions,
            tickets,
            new InspectableIdempotencyStore(),
            new InspectableDistributedCache(),
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        var ticket = await ReadBodyAsync(result, context);
        ticket.CustomerId.ShouldBe(customerId);
    }

    [Fact(DisplayName = "email 與 phone 都沒填 → 422 support.contact-required")]
    public async Task Missing_contact_returns_422()
    {
        var tickets = BuildService(out _);
        var context = Context();

        var result = await SupportEndpoints.CreateTicketAsync(
            new CreateSupportTicketInput("嗨", null, null, [], null),
            context,
            new FakeSessionStore(),
            tickets,
            new InspectableIdempotencyStore(),
            new InspectableDistributedCache(),
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact(DisplayName = "留言超過 2000 字 → 擋下")]
    public async Task Overlong_message_is_rejected()
    {
        var tickets = BuildService(out _);
        var context = Context();

        var result = await SupportEndpoints.CreateTicketAsync(
            new CreateSupportTicketInput(new string('字', 2001), "a@b.com", null, [], null),
            context,
            new FakeSessionStore(),
            tickets,
            new InspectableIdempotencyStore(),
            new InspectableDistributedCache(),
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact(DisplayName = "menuPath 超過 5 層 → 擋下")]
    public async Task Overlong_menu_path_is_rejected()
    {
        var tickets = BuildService(out _);
        var context = Context();

        var result = await SupportEndpoints.CreateTicketAsync(
            new CreateSupportTicketInput(
                "嗨", "a@b.com", null, ["1", "2", "3", "4", "5", "6"], null),
            context,
            new FakeSessionStore(),
            tickets,
            new InspectableIdempotencyStore(),
            new InspectableDistributedCache(),
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact(DisplayName = "同一 IP／購物車每小時超過上限 → 429 support.too-many-requests")]
    public async Task Rate_limit_returns_429_once_exceeded()
    {
        var tickets = BuildService(out _);
        var cache = new InspectableDistributedCache();
        var cartCookie = Guid.NewGuid().ToString("N");
        IResult? last = null;
        HttpContext? lastContext = null;

        for (var i = 0; i < 21; i++)
        {
            var context = Context();
            context.Request.Headers.Cookie = $"gg_cart={cartCookie}";
            last = await SupportEndpoints.CreateTicketAsync(
                new CreateSupportTicketInput($"第 {i} 次留言", "a@b.com", null, [], null),
                context,
                new FakeSessionStore(),
                tickets,
                new InspectableIdempotencyStore(),
                cache,
                NullLogger.Instance,
                TestContext.Current.CancellationToken);
            lastContext = context;
        }

        await last!.ExecuteAsync(lastContext!);
        lastContext!.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
    }

    [Fact(DisplayName = "★ IDistributedCache 讀寫都丟例外時 fail-open：留言仍然收得下來")]
    public async Task Cache_failures_fail_open_instead_of_blocking_submissions()
    {
        var tickets = BuildService(out _);
        var throwingCache = new InspectableDistributedCache { ThrowOnGet = true, ThrowOnSet = true };
        var context = Context();

        var result = await SupportEndpoints.CreateTicketAsync(
            new CreateSupportTicketInput("KV 掛了也要收得到", "a@b.com", null, [], null),
            context,
            new FakeSessionStore(),
            tickets,
            new InspectableIdempotencyStore(),
            throwingCache,
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        var ticket = await ReadBodyAsync(result, context);
        ticket.Message.ShouldBe("KV 掛了也要收得到");
        context.Response.StatusCode.ShouldBe(StatusCodes.Status201Created);
    }

    private static ICustomerServiceTickets BuildService(out InMemoryTicketRepository repository)
    {
        repository = new InMemoryTicketRepository();
        return new CustomerServiceTicketService(
            repository,
            new NoopUnitOfWork(),
            new FakeClock(Now),
            new FakeCorrelationContext());
    }

    private static DefaultHttpContext Context()
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
        context.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("N");
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<SupportTicket> ReadBodyAsync(IResult result, DefaultHttpContext context)
    {
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var ticket = await System.Text.Json.JsonSerializer.DeserializeAsync<SupportTicket>(
            context.Response.Body,
            GreyGray.Shared.Kernel.Json.GreyGrayJson.Options,
            TestContext.Current.CancellationToken);
        return ticket!;
    }
}
