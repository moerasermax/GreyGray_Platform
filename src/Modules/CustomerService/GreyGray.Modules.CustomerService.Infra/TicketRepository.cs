using GreyGray.Modules.CustomerService.Contracts;
using GreyGray.Modules.CustomerService.Core;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.CustomerService.Infra;

internal sealed class TicketRepository(CustomerServiceDbContext dbContext) : ITicketRepository
{
    public void Add(Ticket ticket) => dbContext.Tickets.Add(ticket);

    public Task<Ticket?> FindAsync(
        TicketId id,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        dbContext.Tickets
            .Include(ticket => ticket.Messages)
            .SingleOrDefaultAsync(
                ticket => ticket.Id == id && ticket.TenantId == tenantId,
                cancellationToken);

    public async Task<(IReadOnlyList<Ticket> Items, bool HasNext)> ListAsync(
        TenantId tenantId,
        SupportTicketStatus? status,
        TicketId? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Tickets
            .AsNoTracking()
            .Include(ticket => ticket.Messages)
            .Where(ticket => ticket.TenantId == tenantId);
        if (status is not null)
        {
            query = query.Where(ticket => ticket.Status == status.Value);
        }

        if (cursor is not null)
        {
            var anchor = await dbContext.Tickets
                .AsNoTracking()
                .Where(ticket => ticket.Id == cursor.Value && ticket.TenantId == tenantId)
                .Select(ticket => new { ticket.CreatedAt, ticket.Id })
                .SingleOrDefaultAsync(cancellationToken);
            if (anchor is null)
            {
                return ([], false);
            }

            query = query.Where(ticket => ticket.CreatedAt < anchor.CreatedAt
                || (ticket.CreatedAt == anchor.CreatedAt && ticket.Id < anchor.Id));
        }

        var rows = await query
            .OrderByDescending(ticket => ticket.CreatedAt)
            .ThenByDescending(ticket => ticket.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasNext = rows.Length > limit;
        var page = rows.Take(limit).ToArray();
        return (page, hasNext);
    }
}
