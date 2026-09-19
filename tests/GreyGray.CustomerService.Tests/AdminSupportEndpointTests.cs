using GreyGray.Api.Admin;
using GreyGray.Modules.CustomerService.Contracts;
using GreyGray.Modules.CustomerService.Core;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.CustomerService.Tests;

/// <summary>
/// 直接呼叫 <see cref="SupportEndpoints"/> 抽出來的 handler，驗證分頁／結案的業務邏輯。
/// <b>角色守衛（ReadOnly 不能結案、Operator 可以）不在這裡測</b>——這個 repo 目前沒有
/// route pipeline 測試基礎（比照既有 Admin 端點的慣例，例如 BE-44 的 ReadOnly 403 留給活體），
/// 改用真的 dev Host 走一次驗證，見 <c>.dispatch/reports/BE-55.md</c>。
/// </summary>
public sealed class AdminSupportEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "游標格式錯誤回 Problem，不是 500")]
    public async Task ListTicketsAsync_with_malformed_cursor_returns_problem()
    {
        var tickets = BuildService(out _);
        var context = Context();

        var result = await SupportEndpoints.ListTicketsAsync(
            null, "not-a-guid", null, tickets, TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact(DisplayName = "游標分頁：limit+1 判斷有沒有下一頁，狀態篩選只看 open")]
    public async Task ListTicketsAsync_paginates_with_status_filter()
    {
        var tickets = BuildService(out var repository);
        for (var i = 0; i < 3; i++)
        {
            var ticket = Ticket.Create(
                TenantId.Default, $"問題 {i}", "a@b.com", null, [], null, null, Now.AddMinutes(i)).Value;
            repository.Add(ticket);
        }

        var firstPage = await tickets.ListAsync(
            new AdminTicketListRequest(SupportTicketStatus.Open, null, 2),
            TestContext.Current.CancellationToken);
        firstPage.IsSuccess.ShouldBeTrue();
        firstPage.Value.Items.Count.ShouldBe(2);
        firstPage.Value.NextCursor.ShouldNotBeNull();

        var secondPage = await tickets.ListAsync(
            new AdminTicketListRequest(SupportTicketStatus.Open, firstPage.Value.NextCursor, 2),
            TestContext.Current.CancellationToken);
        secondPage.Value.Items.Count.ShouldBe(1);
        secondPage.Value.NextCursor.ShouldBeNull();
    }

    [Fact(DisplayName = "找不到的工單 ID → Problem")]
    public async Task GetTicketAsync_with_unknown_id_returns_problem()
    {
        var tickets = BuildService(out _);
        var context = Context();

        var result = await SupportEndpoints.GetTicketAsync(
            Guid.NewGuid().ToString("N"), tickets, TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact(DisplayName = "結案成功回 200；重複結案回 409 support.already-resolved")]
    public async Task ResolveTicketAsync_marks_resolved_and_rejects_double_resolution()
    {
        var tickets = BuildService(out var repository);
        var ticket = Ticket.Create(
            TenantId.Default, "需要幫忙", "a@b.com", null, [], null, null, Now).Value;
        repository.Add(ticket);
        var staffId = StaffId.New();
        var context = Context(staffId);

        var firstResult = await SupportEndpoints.ResolveTicketAsync(
            ticket.Id.ToString(),
            new ResolveSupportTicketInput("已回覆客人"),
            context,
            tickets,
            TestContext.Current.CancellationToken);
        await firstResult.ExecuteAsync(context);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);

        var secondContext = Context(staffId);
        var secondResult = await SupportEndpoints.ResolveTicketAsync(
            ticket.Id.ToString(),
            new ResolveSupportTicketInput(null),
            secondContext,
            tickets,
            TestContext.Current.CancellationToken);
        await secondResult.ExecuteAsync(secondContext);
        secondContext.Response.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
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

    private static DefaultHttpContext Context(StaffId? staffId = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpJsonOptions(options =>
            BffHttp.ApplyGreyGrayJson(options.SerializerOptions));
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();
        if (staffId is not null)
        {
            context.Items["greygray.staff-id"] = staffId.Value;
        }

        return context;
    }
}
