using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Identity.Core;

internal sealed class CustomerCredential
{
    private CustomerCredential()
    {
    }

    private CustomerCredential(
        CustomerId customerId,
        TenantId tenantId,
        string phoneLookup,
        string phoneNumberMasked,
        string passwordHash,
        DateTimeOffset createdAt)
    {
        CustomerId = customerId;
        TenantId = tenantId;
        PhoneLookup = phoneLookup;
        PhoneNumberMasked = phoneNumberMasked;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    public CustomerId CustomerId { get; private set; }
    public TenantId TenantId { get; private set; }
    public string PhoneLookup { get; private set; } = string.Empty;
    public string PhoneNumberMasked { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static CustomerCredential Create(
        CustomerId customerId,
        TenantId tenantId,
        string phoneLookup,
        string phoneNumberMasked,
        string passwordHash,
        DateTimeOffset createdAt) =>
        new(customerId, tenantId, phoneLookup, phoneNumberMasked, passwordHash, createdAt);
}

internal sealed class CustomerPrivateProfile
{
    private CustomerPrivateProfile()
    {
    }

    private CustomerPrivateProfile(CustomerId customerId, string? encryptedEmail)
    {
        CustomerId = customerId;
        EncryptedEmail = encryptedEmail;
    }

    public CustomerId CustomerId { get; private set; }
    public string? EncryptedEmail { get; private set; }
    public bool LineLinked { get; private set; }

    public static CustomerPrivateProfile Create(CustomerId customerId, string? encryptedEmail) =>
        new(customerId, encryptedEmail);

    public void UpdateEmail(string? encryptedEmail) => EncryptedEmail = encryptedEmail;
}

internal sealed class StaffAccount
{
    private StaffAccount()
    {
    }

    private StaffAccount(
        StaffId id,
        TenantId tenantId,
        string displayName,
        string emailLookup,
        string encryptedEmail,
        string passwordHash,
        StaffRole role,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        DisplayName = displayName;
        EmailLookup = emailLookup;
        EncryptedEmail = encryptedEmail;
        PasswordHash = passwordHash;
        Role = role;
        IsActive = true;
        CreatedAt = createdAt;
    }

    public StaffId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string EmailLookup { get; private set; } = string.Empty;
    public string EncryptedEmail { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public StaffRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static StaffAccount Create(
        StaffId id,
        TenantId tenantId,
        string displayName,
        string emailLookup,
        string encryptedEmail,
        string passwordHash,
        StaffRole role,
        DateTimeOffset createdAt) =>
        new(id, tenantId, displayName, emailLookup, encryptedEmail, passwordHash, role, createdAt);
}

internal sealed class CustomerAddress
{
    private CustomerAddress()
    {
    }

    private CustomerAddress(
        AddressId id,
        CustomerId customerId,
        TenantId tenantId,
        EncryptedAddressData data,
        bool isDefault,
        DateTimeOffset createdAt)
    {
        Id = id;
        CustomerId = customerId;
        TenantId = tenantId;
        Apply(data, isDefault);
        CreatedAt = createdAt;
    }

    public AddressId Id { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public TenantId TenantId { get; private set; }
    public string RecipientName { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public string PostalCode { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string District { get; private set; } = string.Empty;
    public string StreetAddress { get; private set; } = string.Empty;
    public bool IsDefault { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static CustomerAddress Create(
        AddressId id,
        CustomerId customerId,
        TenantId tenantId,
        EncryptedAddressData data,
        bool isDefault,
        DateTimeOffset createdAt) =>
        new(id, customerId, tenantId, data, isDefault, createdAt);

    public void Update(EncryptedAddressData data, bool isDefault) => Apply(data, isDefault);
    public void ClearDefault() => IsDefault = false;

    private void Apply(EncryptedAddressData data, bool isDefault)
    {
        RecipientName = data.RecipientName;
        PhoneNumber = data.PhoneNumber;
        PostalCode = data.PostalCode;
        City = data.City;
        District = data.District;
        StreetAddress = data.StreetAddress;
        IsDefault = isDefault;
    }
}

internal sealed record EncryptedAddressData(
    string RecipientName,
    string PhoneNumber,
    string PostalCode,
    string City,
    string District,
    string StreetAddress);

internal interface IIdentityRepository
{
    Task<Customer?> FindCustomerAsync(CustomerId id, TenantId tenantId, CancellationToken cancellationToken);
    Task<CustomerCredential?> FindCredentialByPhoneAsync(string phoneLookup, TenantId tenantId, CancellationToken cancellationToken);
    Task<CustomerCredential?> FindCredentialByCustomerAsync(CustomerId id, CancellationToken cancellationToken);
    Task<CustomerPrivateProfile?> FindCustomerProfileAsync(CustomerId id, CancellationToken cancellationToken);
    Task<StaffAccount?> FindStaffByEmailAsync(string emailLookup, TenantId tenantId, CancellationToken cancellationToken);
    Task<StaffAccount?> FindStaffAsync(StaffId id, TenantId tenantId, CancellationToken cancellationToken);
    Task<CustomerAddress?> FindAddressAsync(AddressId id, CustomerId customerId, CancellationToken cancellationToken);
    Task<CustomerAddress?> FindAddressByIdAsync(AddressId id, TenantId tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerAddress>> ListAddressesAsync(CustomerId customerId, CancellationToken cancellationToken);
    void AddCustomer(Customer customer, CustomerCredential credential, CustomerPrivateProfile profile);
    void AddStaff(StaffAccount staff);
    void AddAddress(CustomerAddress address);
    void RemoveAddress(CustomerAddress address);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal interface IIdentityDataProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
    string CreateLookup(string purpose, string normalizedValue);
}

internal sealed class IdentityPersistenceConflictException(
    string conflictCode,
    Exception innerException) : Exception("Identity 唯一性衝突。", innerException)
{
    public string ConflictCode { get; } = conflictCode;
}
