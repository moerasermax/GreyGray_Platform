using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Ordering.Infra;

internal sealed class OrderingRepository(OrderingDbContext dbContext) : IOrderRepository
{
    public Task<Order?> GetAsync(
        TenantId tenantId,
        OrderId orderId,
        CancellationToken cancellationToken) =>
        dbContext.Orders
            .Include(order => order.Lines)
            .SingleOrDefaultAsync(
                order => order.TenantId == tenantId && order.Id == orderId,
                cancellationToken);

    public Task<Order?> GetByCheckoutAsync(
        TenantId tenantId,
        CartId cartId,
        CancellationToken cancellationToken) =>
        dbContext.Orders
            .Include(order => order.Lines)
            .SingleOrDefaultAsync(
                order => order.TenantId == tenantId && order.CheckoutCartId == cartId,
                cancellationToken);

    public async Task<OrderQueryPage> ListCustomerAsync(
        TenantId tenantId,
        CustomerOrderListRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Lines)
            .Where(order => order.TenantId == tenantId && order.CustomerId == request.CustomerId);
        if (request.Status is not null)
        {
            query = query.Where(order => order.Status == request.Status.Value);
        }

        return await PageAsync(query, request.Cursor, request.Limit, cancellationToken);
    }

    public async Task<OrderQueryPage> ListAdminAsync(
        TenantId tenantId,
        AdminOrderListRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Lines)
            .Where(order => order.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var normalized = request.Query.Trim();
            query = query.Where(order => order.OrderNumber.Contains(normalized));
        }

        if (request.Status is not null)
        {
            query = query.Where(order => order.Status == request.Status.Value);
        }

        if (request.CampaignId is not null)
        {
            query = query.Where(order => order.Lines.Any(line =>
                line.CampaignId == request.CampaignId.Value));
        }

        return await PageAsync(query, request.Cursor, request.Limit, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetByCampaignAsync(
        TenantId tenantId,
        CampaignId campaignId,
        CancellationToken cancellationToken) =>
        await dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Lines)
            .Where(order => order.TenantId == tenantId
                && order.Lines.Any(line => line.CampaignId == campaignId))
            .OrderByDescending(order => order.PlacedAt)
            .ToArrayAsync(cancellationToken);

    public Task<Order?> GetByLineAsync(
        TenantId tenantId,
        OrderLineId orderLineId,
        CancellationToken cancellationToken) =>
        dbContext.Orders
            .Include(order => order.Lines)
            .SingleOrDefaultAsync(
                order => order.TenantId == tenantId
                    && order.Lines.Any(line => line.Id == orderLineId),
                cancellationToken);

    public void Add(Order order) => dbContext.Orders.Add(order);

    private async Task<OrderQueryPage> PageAsync(
        IQueryable<Order> query,
        OrderId? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        if (cursor is not null)
        {
            var cursorOrder = await dbContext.Orders
                .AsNoTracking()
                .Where(order => order.Id == cursor.Value)
                .Select(order => new { order.PlacedAt, order.Id })
                .SingleOrDefaultAsync(cancellationToken);
            if (cursorOrder is null)
            {
                return new OrderQueryPage([], null);
            }

            query = query.Where(order => order.PlacedAt < cursorOrder.PlacedAt
                || (order.PlacedAt == cursorOrder.PlacedAt
                    && order.Id < cursorOrder.Id));
        }

        var rows = await query
            .OrderByDescending(order => order.PlacedAt)
            .ThenByDescending(order => order.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasNext = rows.Length > limit;
        var page = rows.Take(limit).ToArray();
        return new OrderQueryPage(page, hasNext ? page[^1].Id : null);
    }
}
