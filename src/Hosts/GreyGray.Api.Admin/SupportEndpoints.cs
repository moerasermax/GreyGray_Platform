using System.Runtime.CompilerServices;
using GreyGray.Modules.CustomerService.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Http;
using GreyGray.Shared.Kernel;

// M1aEndpoints.cs 已經有一份同類宣告，但那個檔不在 BE-55 的 allow 裡（BE-54 同波在改）；
// 這個模組自己的測試需要碰 internal 型別，放在自己擁有的檔案裡宣告。
[assembly: InternalsVisibleTo("GreyGray.CustomerService.Tests")]

namespace GreyGray.Api.Admin;

/// <summary>
/// 客服工單——後台列表、單筆與結案（ADR-040）。獨立檔，<b>不改 <see cref="M1aEndpoints"/></b>：
/// BE-54 同一波在改那個檔，兩包同時動同一個檔一定撞。
/// </summary>
internal static class SupportEndpoints
{
    /// <summary>
    /// 跟 <see cref="M1aEndpoints"/> 的 <c>StaffItem</c> 是同一把 key——那個常數是 <c>private</c>，
    /// <see cref="M1aEndpoints.StaffRoleFilter"/> 通過後把員工身分放在這裡，兩邊必須一致。
    /// </summary>
    private const string StaffItem = "greygray.staff-id";

    public static IEndpointRouteBuilder MapSupportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/v1");

        api.MapGet("/support/tickets", async (
            SupportTicketStatus? status,
            string? cursor,
            int? limit,
            ICustomerServiceTickets tickets,
            CancellationToken cancellationToken) =>
            await ListTicketsAsync(status, cursor, limit, tickets, cancellationToken))
            .AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.ReadOnly));

        api.MapGet("/support/tickets/{ticketId}", async (
            string ticketId,
            ICustomerServiceTickets tickets,
            CancellationToken cancellationToken) =>
            await GetTicketAsync(ticketId, tickets, cancellationToken))
            .AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.ReadOnly));

        api.MapPost("/support/tickets/{ticketId}/resolve", async (
            string ticketId,
            ResolveSupportTicketInput input,
            HttpContext context,
            ICustomerServiceTickets tickets,
            CancellationToken cancellationToken) =>
            await ResolveTicketAsync(ticketId, input, context, tickets, cancellationToken))
            .AddEndpointFilter(new M1aEndpoints.StaffRoleFilter(StaffRole.Operator));

        return endpoints;
    }

    internal static async Task<IResult> ListTicketsAsync(
        SupportTicketStatus? status,
        string? cursor,
        int? limit,
        ICustomerServiceTickets tickets,
        CancellationToken cancellationToken)
    {
        TicketId? pageCursor = null;
        if (cursor is not null)
        {
            if (!M1aEndpoints.TryId(cursor, out var parsedCursor))
            {
                return BffHttp.Problem(new Error(
                    "support.invalid-cursor", "分頁游標格式錯誤。"));
            }

            pageCursor = new TicketId(parsedCursor);
        }

        var result = await tickets.ListAsync(
            new AdminTicketListRequest(status, pageCursor, limit ?? 20),
            cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : BffHttp.Problem(result.Error);
    }

    internal static async Task<IResult> GetTicketAsync(
        string ticketId,
        ICustomerServiceTickets tickets,
        CancellationToken cancellationToken)
    {
        if (!M1aEndpoints.TryId(ticketId, out var parsed))
        {
            return BffHttp.Problem(new Error("support.ticket-not-found", "找不到指定的客服工單。"));
        }

        var result = await tickets.GetAsync(new TicketId(parsed), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : BffHttp.Problem(result.Error);
    }

    internal static async Task<IResult> ResolveTicketAsync(
        string ticketId,
        ResolveSupportTicketInput input,
        HttpContext context,
        ICustomerServiceTickets tickets,
        CancellationToken cancellationToken)
    {
        if (!M1aEndpoints.TryId(ticketId, out var parsed))
        {
            return BffHttp.Problem(new Error("support.ticket-not-found", "找不到指定的客服工單。"));
        }

        var staffId = GetStaffId(context);
        var result = await tickets.ResolveAsync(
            new TicketId(parsed),
            staffId,
            input.StaffNote,
            cancellationToken);
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        // BffHttp.Problem 的預設 StatusFor 只認得 "already-paid"／"cancelled"，
        // "support.already-resolved" 不在裡面會落到預設 422，契約要求的是 409。
        return result.Error.Code == "support.already-resolved"
            ? BffHttp.Problem(result.Error, StatusCodes.Status409Conflict)
            : BffHttp.Problem(result.Error);
    }

    private static StaffId GetStaffId(HttpContext context) =>
        context.Items[StaffItem] is StaffId id
            ? id
            : throw new InvalidOperationException("StaffRoleFilter 尚未建立員工身分。");
}

internal sealed record ResolveSupportTicketInput(string? StaffNote);
