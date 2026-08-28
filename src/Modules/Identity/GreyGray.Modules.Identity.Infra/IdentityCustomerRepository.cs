using GreyGray.Modules.Identity.Core;

namespace GreyGray.Modules.Identity.Infra;

internal sealed class IdentityCustomerRepository(IdentityDbContext dbContext)
    : ICustomerRepository
{
    public void Add(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        dbContext.Customers.Add(customer);
    }
}
