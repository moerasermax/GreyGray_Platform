using GreyGray.Api.Admin;
using GreyGray.Modules.CustomerService.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.CustomerService.Tests;

public sealed class AdminSupportStatusBindingTests
{
    [Fact(DisplayName = "客服列表 status 契約值會經 ASP.NET Core 綁定；省略為全部；非法值回 400")]
    public async Task Status_query_uses_contract_values_through_aspnet_core_binding()
    {
        var tickets = new RecordingTicketService();
        var sessions = new FakeSessionStore();
        var staffId = StaffId.New();
        var sessionToken = sessions.Issue(SessionSubjectKind.Staff, staffId.ToString(), StaffRole.ReadOnly.ToString());

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddLogging();
        builder.Services.ConfigureHttpJsonOptions(options =>
            BffHttp.ApplyGreyGrayJson(options.SerializerOptions));
        builder.Services.AddSingleton<ICustomerServiceTickets>(tickets);
        builder.Services.AddSingleton<ISessionStore>(sessions);
        builder.Services.AddSingleton<IStaffDirectory>(new ReadOnlyStaffDirectory(staffId));
        builder.Services.AddSingleton<IStaffRolePolicy, AllowReadOnlyRolePolicy>();

        await using var app = builder.Build();
        app.MapSupportEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Add("Cookie", $"gg_admin_session={sessionToken}");

        var open = await client.GetAsync(
            "/v1/support/tickets?status=open",
            TestContext.Current.CancellationToken);
        var resolved = await client.GetAsync(
            "/v1/support/tickets?status=resolved",
            TestContext.Current.CancellationToken);
        var omitted = await client.GetAsync(
            "/v1/support/tickets",
            TestContext.Current.CancellationToken);
        var invalid = await client.GetAsync(
            "/v1/support/tickets?status=bogus",
            TestContext.Current.CancellationToken);

        open.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        resolved.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        omitted.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        invalid.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        tickets.ObservedStatuses.ShouldBe([
            SupportTicketStatus.Open,
            SupportTicketStatus.Resolved,
            null,
        ]);

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class RecordingTicketService : ICustomerServiceTickets
    {
        public List<SupportTicketStatus?> ObservedStatuses { get; } = [];

        public Task<Result<SupportTicketPage>> ListAsync(
            AdminTicketListRequest request,
            CancellationToken cancellationToken)
        {
            ObservedStatuses.Add(request.Status);
            return Task.FromResult(Result<SupportTicketPage>.Success(new SupportTicketPage([], null)));
        }

        public Task<Result<SupportTicket>> CreateAsync(
            CreateSupportTicketRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<SupportTicket>> GetAsync(
            TicketId id,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<SupportTicket>> ResolveAsync(
            TicketId id,
            StaffId staffId,
            string? staffNote,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ReadOnlyStaffDirectory(StaffId expectedStaffId) : IStaffDirectory
    {
        public Task<Result<StaffRole>> GetRoleAsync(StaffId id, CancellationToken cancellationToken)
        {
            id.ShouldBe(expectedStaffId);
            return Task.FromResult(Result<StaffRole>.Success(StaffRole.ReadOnly));
        }
    }

    private sealed class AllowReadOnlyRolePolicy : IStaffRolePolicy
    {
        public bool Allows(StaffRole actualRole, StaffRole requiredRole) =>
            actualRole == StaffRole.ReadOnly && requiredRole == StaffRole.ReadOnly;
    }
}
