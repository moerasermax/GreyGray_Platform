using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Audit;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Identity.Core;

internal sealed class CustomerDirectoryService(
    IIdentityRepository repository,
    IIdentityDataProtector dataProtector,
    IAuditWriter? auditWriter,
    ICorrelationContext correlationContext) : ICustomerDirectory
{
    public async Task<Result<CustomerSummary>> GetAsync(
        CustomerId id,
        CancellationToken cancellationToken)
    {
        var customer = await repository.FindCustomerAsync(
            id,
            correlationContext.TenantId,
            cancellationToken);
        return customer is null
            ? Result<CustomerSummary>.Failure("identity.customer-not-found", "找不到客戶。")
            : customer.ToSummary();
    }

    public async Task<Result<CustomerContact>> GetContactAsync(
        CustomerId id,
        StaffId? actor,
        string accessReason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessReason))
        {
            return Result<CustomerContact>.Failure(
                "identity.access-reason-required",
                "讀取客戶聯絡資料必須填寫理由。");
        }

        if (auditWriter is null)
        {
            return Result<CustomerContact>.Failure(
                "audit.writer-unavailable",
                "稽核寫入服務未啟用，拒絕讀取個資明文。");
        }

        var customer = await repository.FindCustomerAsync(
            id,
            correlationContext.TenantId,
            cancellationToken);
        var profile = await repository.FindCustomerProfileAsync(id, cancellationToken);
        var addresses = await repository.ListAddressesAsync(id, cancellationToken);
        var address = addresses.FirstOrDefault();
        if (customer is null || profile is null || address is null)
        {
            return Result<CustomerContact>.Failure(
                "identity.contact-not-found",
                "客戶尚未建立完整聯絡資料。");
        }

        await auditWriter.WriteAsync(
            AuditCategory.PersonalDataAccess,
            "customer.contact.read",
            "Customer",
            id.ToString(),
            actor?.Value,
            id.Value,
            accessReason.Trim(),
            "{}",
            cancellationToken);

        return new CustomerContact(
            id,
            dataProtector.Unprotect(address.RecipientName),
            dataProtector.Unprotect(address.PhoneNumber),
            profile.EncryptedEmail is null ? null : dataProtector.Unprotect(profile.EncryptedEmail),
            null);
    }

    public async Task<Result<ShippingAddress>> GetAddressAsync(
        AddressId id,
        CancellationToken cancellationToken)
    {
        var address = await repository.FindAddressByIdAsync(
            id,
            correlationContext.TenantId,
            cancellationToken);
        return address is null
            ? Result<ShippingAddress>.Failure("identity.address-not-found", "找不到地址。")
            : new ShippingAddress(
                address.Id,
                address.CustomerId,
                dataProtector.Unprotect(address.RecipientName),
                dataProtector.Unprotect(address.PhoneNumber),
                dataProtector.Unprotect(address.PostalCode),
                dataProtector.Unprotect(address.City),
                dataProtector.Unprotect(address.District),
                dataProtector.Unprotect(address.StreetAddress));
    }
}
