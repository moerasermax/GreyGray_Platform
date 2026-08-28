using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Checkout.Core;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Checkout.Infra;

internal sealed class CheckoutRepository(CheckoutDbContext dbContext) : ICartRepository
{
    public Task<Cart?> GetAsync(
        TenantId tenantId,
        CartId cartId,
        CancellationToken cancellationToken) =>
        dbContext.Carts
            .Include(cart => cart.Lines)
            .SingleOrDefaultAsync(
                cart => cart.TenantId == tenantId && cart.Id == cartId,
                cancellationToken);

    public void Add(Cart cart) => dbContext.Carts.Add(cart);
}
