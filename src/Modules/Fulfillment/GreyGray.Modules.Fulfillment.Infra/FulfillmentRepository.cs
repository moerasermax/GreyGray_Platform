using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Fulfillment.Core;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Fulfillment.Infra;

internal sealed class FulfillmentRepository(FulfillmentDbContext dbContext) : IShipmentRepository
{
    public Task<ShipmentAggregate?> GetAsync(
        TenantId tenantId,
        ShipmentId id,
        CancellationToken cancellationToken) =>
        dbContext.Shipments
            .Include(shipment => shipment.OrderLinks)
            .SingleOrDefaultAsync(
                shipment => shipment.TenantId == tenantId && shipment.Id == id,
                cancellationToken);

    public async Task<IReadOnlyList<ShipmentAggregate>> GetByOrderAsync(
        TenantId tenantId,
        OrderId orderId,
        CancellationToken cancellationToken) =>
        await dbContext.Shipments
            .AsNoTracking()
            .Include(shipment => shipment.OrderLinks)
            .Where(shipment => shipment.TenantId == tenantId
                && shipment.OrderLinks.Any(link => link.OrderId == orderId))
            .OrderByDescending(shipment => shipment.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public async Task<ShipmentQueryPage> ListAsync(
        TenantId tenantId,
        AdminShipmentListRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Shipments
            .AsNoTracking()
            .Include(shipment => shipment.OrderLinks)
            .Where(shipment => shipment.TenantId == tenantId);
        if (request.Status is not null)
        {
            query = query.Where(shipment => shipment.Status == request.Status.Value);
        }

        if (request.Cursor is { } cursor)
        {
            var cursorShipment = await dbContext.Shipments
                .AsNoTracking()
                .Where(shipment => shipment.Id == cursor)
                .Select(shipment => new { shipment.CreatedAt, shipment.Id })
                .SingleOrDefaultAsync(cancellationToken);
            if (cursorShipment is null)
            {
                return new ShipmentQueryPage([], null);
            }

            query = query.Where(shipment => shipment.CreatedAt < cursorShipment.CreatedAt
                || (shipment.CreatedAt == cursorShipment.CreatedAt
                    && shipment.Id < cursorShipment.Id));
        }

        var limit = request.Limit is > 0 ? request.Limit : 20;
        var rows = await query
            .OrderByDescending(shipment => shipment.CreatedAt)
            .ThenByDescending(shipment => shipment.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasNext = rows.Length > limit;
        var page = rows.Take(limit).ToArray();
        return new ShipmentQueryPage(page, hasNext ? page[^1].Id : null);
    }

    public void Add(ShipmentAggregate shipment) => dbContext.Shipments.Add(shipment);
}
