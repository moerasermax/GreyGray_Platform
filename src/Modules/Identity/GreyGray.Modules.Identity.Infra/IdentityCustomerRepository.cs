using GreyGray.Modules.Identity.Core;

using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GreyGray.Modules.Identity.Infra;

internal sealed class IdentityCustomerRepository(IdentityDbContext dbContext)
    : ICustomerRepository, IIdentityRepository
{
    public void Add(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        dbContext.Customers.Add(customer);
    }

    public Task<Customer?> FindCustomerAsync(
        CustomerId id,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        dbContext.Customers.SingleOrDefaultAsync(
            value => value.Id == id && value.TenantId == tenantId,
            cancellationToken);

    public Task<CustomerCredential?> FindCredentialByPhoneAsync(
        string phoneLookup,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        dbContext.CustomerCredentials.SingleOrDefaultAsync(
            value => value.PhoneLookup == phoneLookup && value.TenantId == tenantId,
            cancellationToken);

    public Task<CustomerCredential?> FindCredentialByCustomerAsync(
        CustomerId id,
        CancellationToken cancellationToken) =>
        dbContext.CustomerCredentials.SingleOrDefaultAsync(
            value => value.CustomerId == id,
            cancellationToken);

    public Task<CustomerPrivateProfile?> FindCustomerProfileAsync(
        CustomerId id,
        CancellationToken cancellationToken) =>
        dbContext.CustomerProfiles.SingleOrDefaultAsync(
            value => value.CustomerId == id,
            cancellationToken);

    public Task<StaffAccount?> FindStaffByEmailAsync(
        string emailLookup,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        dbContext.StaffAccounts.SingleOrDefaultAsync(
            value => value.EmailLookup == emailLookup && value.TenantId == tenantId,
            cancellationToken);

    public Task<StaffAccount?> FindStaffAsync(
        StaffId id,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        dbContext.StaffAccounts.SingleOrDefaultAsync(
            value => value.Id == id && value.TenantId == tenantId,
            cancellationToken);

    public Task<CustomerAddress?> FindAddressAsync(
        AddressId id,
        CustomerId customerId,
        CancellationToken cancellationToken) =>
        dbContext.CustomerAddresses.SingleOrDefaultAsync(
            value => value.Id == id && value.CustomerId == customerId,
            cancellationToken);

    public Task<CustomerAddress?> FindAddressByIdAsync(
        AddressId id,
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        dbContext.CustomerAddresses.SingleOrDefaultAsync(
            value => value.Id == id && value.TenantId == tenantId,
            cancellationToken);

    public async Task<IReadOnlyList<CustomerAddress>> ListAddressesAsync(
        CustomerId customerId,
        CancellationToken cancellationToken) =>
        await dbContext.CustomerAddresses
            .Where(value => value.CustomerId == customerId)
            .OrderByDescending(value => value.IsDefault)
            .ThenBy(value => value.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public void AddCustomer(
        Customer customer,
        CustomerCredential credential,
        CustomerPrivateProfile profile)
    {
        dbContext.Customers.Add(customer);
        dbContext.CustomerCredentials.Add(credential);
        dbContext.CustomerProfiles.Add(profile);
    }

    public void AddStaff(StaffAccount staff) => dbContext.StaffAccounts.Add(staff);
    public void AddAddress(CustomerAddress address) => dbContext.CustomerAddresses.Add(address);
    public void RemoveAddress(CustomerAddress address) => dbContext.CustomerAddresses.Remove(address);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
        {
            var conflictCode = postgres.ConstraintName switch
            {
                "ux_customer_credential_tenant_phone" => "identity.phone-already-registered",
                "ux_staff_account_tenant_email" => "identity.staff-email-conflict",
                _ => "identity.unique-conflict",
            };
            throw new IdentityPersistenceConflictException(conflictCode, exception);
        }
    }
}
