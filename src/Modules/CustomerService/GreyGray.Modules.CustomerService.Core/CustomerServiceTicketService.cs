using GreyGray.Modules.CustomerService.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.CustomerService.Core;

internal interface ITicketRepository
{
    void Add(Ticket ticket);

    Task<Ticket?> FindAsync(TicketId id, TenantId tenantId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Ticket> Items, bool HasNext)> ListAsync(
        TenantId tenantId,
        SupportTicketStatus? status,
        TicketId? cursor,
        int limit,
        CancellationToken cancellationToken);
}

internal sealed class CustomerServiceTicketService(
    ITicketRepository tickets,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICorrelationContext correlationContext) : ICustomerServiceTickets
{
    public async Task<Result<SupportTicket>> CreateAsync(
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = Ticket.Create(
            correlationContext.TenantId,
            request.Message,
            request.ContactEmail,
            request.ContactPhone,
            request.MenuPath,
            request.OrderId,
            request.CustomerId,
            clock.UtcNow);
        if (created.IsFailure)
        {
            return Result<SupportTicket>.Failure(created.Error);
        }

        tickets.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return created.Value.ToContract();
    }

    public async Task<Result<SupportTicketPage>> ListAsync(
        AdminTicketListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Limit is < 1 or > 100)
        {
            return Result<SupportTicketPage>.Failure(
                "support.invalid-page-limit", "分頁筆數必須介於 1 到 100 之間。");
        }

        var (items, hasNext) = await tickets.ListAsync(
            correlationContext.TenantId,
            request.Status,
            request.Cursor,
            request.Limit,
            cancellationToken);
        var page = items.Select(ticket => ticket.ToContract()).ToArray();
        return new SupportTicketPage(page, hasNext ? items[^1].Id : null);
    }

    public async Task<Result<SupportTicket>> GetAsync(
        TicketId id,
        CancellationToken cancellationToken)
    {
        var ticket = await tickets.FindAsync(id, correlationContext.TenantId, cancellationToken);
        return ticket is null
            ? Result<SupportTicket>.Failure("support.ticket-not-found", "找不到指定的客服工單。")
            : ticket.ToContract();
    }

    public async Task<Result<SupportTicket>> ResolveAsync(
        TicketId id,
        StaffId staffId,
        string? staffNote,
        CancellationToken cancellationToken)
    {
        var ticket = await tickets.FindAsync(id, correlationContext.TenantId, cancellationToken);
        if (ticket is null)
        {
            return Result<SupportTicket>.Failure("support.ticket-not-found", "找不到指定的客服工單。");
        }

        var resolved = ticket.Resolve(staffId, staffNote, clock.UtcNow);
        if (resolved.IsFailure)
        {
            return Result<SupportTicket>.Failure(resolved.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ticket.ToContract();
    }
}
