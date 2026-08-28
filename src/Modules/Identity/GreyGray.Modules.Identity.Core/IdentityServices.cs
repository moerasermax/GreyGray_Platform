using System.Net.Mail;
using System.Text.RegularExpressions;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Identity.Core;

internal sealed partial class CustomerAccountService(
    IIdentityRepository repository,
    IIdentityDataProtector dataProtector,
    PasswordHasher passwordHasher,
    IEventPublisher eventPublisher,
    IClock clock,
    ICorrelationContext correlationContext) : ICustomerAccounts
{
    public async Task<Result<CustomerProfile>> RegisterAsync(
        RegisterCustomerInput input,
        CancellationToken cancellationToken)
    {
        var phone = NormalizePhone(input.PhoneNumber);
        if (phone is null)
        {
            return Failure<CustomerProfile>("identity.invalid-phone", "請輸入有效的台灣手機號碼。");
        }

        if (!IsValidPassword(input.Password))
        {
            return Failure<CustomerProfile>("identity.weak-password", "密碼長度須為 8 到 128 個字元。");
        }

        var displayName = NormalizeDisplayName(input.DisplayName);
        if (displayName is null)
        {
            return Failure<CustomerProfile>("identity.invalid-display-name", "顯示名稱須為 1 到 50 個字元。");
        }

        var email = NormalizeOptionalEmail(input.Email);
        if (input.Email is not null && email is null)
        {
            return Failure<CustomerProfile>("identity.invalid-email", "請輸入有效的電子郵件地址。");
        }

        if (input.ReferralCode?.Trim().Length > 32)
        {
            return Failure<CustomerProfile>("identity.invalid-referral-code", "推薦碼不得超過 32 個字元。");
        }

        var tenantId = correlationContext.TenantId;
        var lookup = dataProtector.CreateLookup("customer-phone", phone);
        if (await repository.FindCredentialByPhoneAsync(lookup, tenantId, cancellationToken) is not null)
        {
            return Failure<CustomerProfile>("identity.phone-already-registered", "此手機號碼已註冊。");
        }

        var now = clock.UtcNow;
        var customerId = CustomerId.New();
        var customer = Customer.Register(customerId, tenantId, displayName, now);
        var credential = CustomerCredential.Create(
            customerId,
            tenantId,
            lookup,
            MaskPhone(phone),
            passwordHasher.Hash(input.Password),
            now);
        var profile = CustomerPrivateProfile.Create(
            customerId,
            email is null ? null : dataProtector.Protect(email));

        repository.AddCustomer(customer, credential, profile);
        await eventPublisher.PublishAsync(
            new CustomerRegistered(Guid.CreateVersion7(), now, tenantId, customerId, displayName),
            cancellationToken);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (IdentityPersistenceConflictException exception)
            when (exception.ConflictCode == "identity.phone-already-registered")
        {
            return Failure<CustomerProfile>(
                "identity.phone-already-registered",
                "此手機號碼已註冊。");
        }

        return ToProfile(customer, credential, profile);
    }

    public async Task<Result<CustomerProfile>> AuthenticateAsync(
        CustomerLoginInput input,
        CancellationToken cancellationToken)
    {
        var phone = NormalizePhone(input.PhoneNumber);
        var credential = phone is null
            ? null
            : await repository.FindCredentialByPhoneAsync(
                dataProtector.CreateLookup("customer-phone", phone),
                correlationContext.TenantId,
                cancellationToken);

        // 即使帳號不存在也執行一次完整 PBKDF2；缺帳號、錯密碼、停權都回同一錯誤。
        var validPassword = passwordHasher.Verify(input.Password ?? string.Empty, credential?.PasswordHash);
        var customer = credential is null
            ? null
            : await repository.FindCustomerAsync(
                credential.CustomerId,
                correlationContext.TenantId,
                cancellationToken);

        if (!validPassword || customer is null || !customer.IsActive)
        {
            return InvalidCredentials<CustomerProfile>();
        }

        var profile = await repository.FindCustomerProfileAsync(customer.Id, cancellationToken);
        return profile is null
            ? Result<CustomerProfile>.Failure("identity.profile-missing", "客戶資料不完整，請聯絡客服。")
            : ToProfile(customer, credential!, profile);
    }

    public async Task<Result<CustomerProfile>> GetProfileAsync(
        CustomerId customerId,
        CancellationToken cancellationToken)
    {
        var customer = await repository.FindCustomerAsync(
            customerId,
            correlationContext.TenantId,
            cancellationToken);
        if (customer is null)
        {
            return Result<CustomerProfile>.Failure("identity.customer-not-found", "找不到客戶。");
        }

        var profile = await repository.FindCustomerProfileAsync(customerId, cancellationToken);
        var addressesCredential = await FindCredentialForCustomerAsync(customerId, cancellationToken);
        return profile is null || addressesCredential is null
            ? Result<CustomerProfile>.Failure("identity.profile-missing", "客戶資料不完整，請聯絡客服。")
            : ToProfile(customer, addressesCredential, profile);
    }

    public async Task<Result<CustomerProfile>> UpdateProfileAsync(
        CustomerId customerId,
        UpdateCustomerProfileInput input,
        CancellationToken cancellationToken)
    {
        var customer = await repository.FindCustomerAsync(
            customerId,
            correlationContext.TenantId,
            cancellationToken);
        var profile = await repository.FindCustomerProfileAsync(customerId, cancellationToken);
        if (customer is null || profile is null)
        {
            return Result<CustomerProfile>.Failure("identity.customer-not-found", "找不到客戶。");
        }

        if (input.DisplayName is not null)
        {
            var name = NormalizeDisplayName(input.DisplayName);
            if (name is null)
            {
                return Failure<CustomerProfile>("identity.invalid-display-name", "顯示名稱須為 1 到 50 個字元。");
            }

            customer.UpdateDisplayName(name);
        }

        if (input.Email is not null || input.EmailSpecified)
        {
            if (input.Email is null)
            {
                profile.UpdateEmail(null);
            }
            else
            {
                var email = NormalizeOptionalEmail(input.Email);
                if (email is null)
                {
                    return Failure<CustomerProfile>("identity.invalid-email", "請輸入有效的電子郵件地址。");
                }

                profile.UpdateEmail(dataProtector.Protect(email));
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
        var credential = await FindCredentialForCustomerAsync(customerId, cancellationToken);
        return credential is null
            ? Result<CustomerProfile>.Failure("identity.profile-missing", "客戶資料不完整，請聯絡客服。")
            : ToProfile(customer, credential, profile);
    }

    private async Task<CustomerCredential?> FindCredentialForCustomerAsync(
        CustomerId customerId,
        CancellationToken cancellationToken) =>
        await repository.FindCredentialByCustomerAsync(customerId, cancellationToken);

    private CustomerProfile ToProfile(
        Customer customer,
        CustomerCredential credential,
        CustomerPrivateProfile profile) =>
        new(
            customer.Id,
            customer.DisplayName,
            customer.Tier,
            customer.IsActive,
            credential.PhoneNumberMasked,
            profile.EncryptedEmail is null ? null : dataProtector.Unprotect(profile.EncryptedEmail),
            profile.LineLinked);

    private static Result<T> Failure<T>(string code, string message) => Result<T>.Failure(code, message);
    private static Result<T> InvalidCredentials<T>() =>
        Result<T>.Failure("identity.invalid-credentials", "帳號或密碼錯誤。");

    private static string? NormalizePhone(string? value)
    {
        var normalized = value?.Trim();
        return normalized is not null && TaiwanMobileRegex().IsMatch(normalized) ? normalized : null;
    }

    private static string? NormalizeDisplayName(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) || normalized.Length > 50 ? null : normalized;
    }

    private static string? NormalizeOptionalEmail(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return normalized.Length <= 254 && MailAddress.TryCreate(normalized, out _)
            ? normalized.ToLowerInvariant()
            : null;
    }

    private static bool IsValidPassword(string? password) => password is { Length: >= 8 and <= 128 };
    private static string MaskPhone(string phone) => string.Concat(phone.AsSpan(0, 4), "***", phone.AsSpan(7, 3));

    [GeneratedRegex("^09[0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex TaiwanMobileRegex();
}

internal sealed class CustomerAddressService(
    IIdentityRepository repository,
    IIdentityDataProtector dataProtector,
    IClock clock,
    ICorrelationContext correlationContext) : ICustomerAddressBook
{
    public async Task<Result<IReadOnlyList<CustomerShippingAddress>>> ListAsync(
        CustomerId customerId,
        CancellationToken cancellationToken)
    {
        if (await FindCustomerAsync(customerId, cancellationToken) is null)
        {
            return Result<IReadOnlyList<CustomerShippingAddress>>.Failure(
                "identity.customer-not-found", "找不到客戶。");
        }

        var addresses = await repository.ListAddressesAsync(customerId, cancellationToken);
        return addresses.Select(ToView).ToArray();
    }

    public async Task<Result<CustomerShippingAddress>> AddAsync(
        CustomerId customerId,
        ShippingAddressInput input,
        CancellationToken cancellationToken)
    {
        if (await FindCustomerAsync(customerId, cancellationToken) is null)
        {
            return NotFound();
        }

        var validated = Validate(input);
        if (validated.IsFailure)
        {
            return Result<CustomerShippingAddress>.Failure(validated.Error);
        }

        var existing = await repository.ListAddressesAsync(customerId, cancellationToken);
        var makeDefault = input.IsDefault || existing.Count == 0;
        if (makeDefault)
        {
            foreach (var existingAddress in existing)
            {
                existingAddress.ClearDefault();
            }
        }

        var address = CustomerAddress.Create(
            AddressId.New(),
            customerId,
            correlationContext.TenantId,
            Protect(validated.Value),
            makeDefault,
            clock.UtcNow);
        repository.AddAddress(address);
        await repository.SaveChangesAsync(cancellationToken);
        return ToView(address);
    }

    public async Task<Result<CustomerShippingAddress>> UpdateAsync(
        CustomerId customerId,
        AddressId addressId,
        ShippingAddressInput input,
        CancellationToken cancellationToken)
    {
        var address = await repository.FindAddressAsync(addressId, customerId, cancellationToken);
        if (address is null)
        {
            return NotFound();
        }

        var validated = Validate(input);
        if (validated.IsFailure)
        {
            return Result<CustomerShippingAddress>.Failure(validated.Error);
        }

        if (input.IsDefault)
        {
            foreach (var sibling in await repository.ListAddressesAsync(customerId, cancellationToken))
            {
                sibling.ClearDefault();
            }
        }

        address.Update(Protect(validated.Value), input.IsDefault);
        await repository.SaveChangesAsync(cancellationToken);
        return ToView(address);
    }

    public async Task<Result> DeleteAsync(
        CustomerId customerId,
        AddressId addressId,
        CancellationToken cancellationToken)
    {
        var address = await repository.FindAddressAsync(addressId, customerId, cancellationToken);
        if (address is null)
        {
            return Result.Failure("identity.address-not-found", "找不到地址。");
        }

        repository.RemoveAddress(address);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private Task<Customer?> FindCustomerAsync(CustomerId id, CancellationToken cancellationToken) =>
        repository.FindCustomerAsync(id, correlationContext.TenantId, cancellationToken);

    private Result<ShippingAddressInput> Validate(ShippingAddressInput input)
    {
        if (string.IsNullOrWhiteSpace(input.RecipientName) || input.RecipientName.Trim().Length > 50 ||
            string.IsNullOrWhiteSpace(input.PhoneNumber) || input.PhoneNumber.Trim().Length > 20 ||
            string.IsNullOrWhiteSpace(input.PostalCode) || input.PostalCode.Trim().Length > 6 ||
            string.IsNullOrWhiteSpace(input.City) || input.City.Trim().Length > 20 ||
            string.IsNullOrWhiteSpace(input.District) || input.District.Trim().Length > 20 ||
            string.IsNullOrWhiteSpace(input.StreetAddress) || input.StreetAddress.Trim().Length > 200)
        {
            return Result<ShippingAddressInput>.Failure("identity.invalid-address", "地址欄位不完整或超過長度限制。");
        }

        return input with
        {
            RecipientName = input.RecipientName.Trim(),
            PhoneNumber = input.PhoneNumber.Trim(),
            PostalCode = input.PostalCode.Trim(),
            City = input.City.Trim(),
            District = input.District.Trim(),
            StreetAddress = input.StreetAddress.Trim(),
        };
    }

    private EncryptedAddressData Protect(ShippingAddressInput input) => new(
        dataProtector.Protect(input.RecipientName),
        dataProtector.Protect(input.PhoneNumber),
        dataProtector.Protect(input.PostalCode),
        dataProtector.Protect(input.City),
        dataProtector.Protect(input.District),
        dataProtector.Protect(input.StreetAddress));

    private CustomerShippingAddress ToView(CustomerAddress address) => new(
        address.Id,
        dataProtector.Unprotect(address.RecipientName),
        dataProtector.Unprotect(address.PhoneNumber),
        dataProtector.Unprotect(address.PostalCode),
        dataProtector.Unprotect(address.City),
        dataProtector.Unprotect(address.District),
        dataProtector.Unprotect(address.StreetAddress),
        address.IsDefault);

    private static Result<CustomerShippingAddress> NotFound() =>
        Result<CustomerShippingAddress>.Failure("identity.address-not-found", "找不到地址。");
}

internal sealed class StaffAccountService(
    IIdentityRepository repository,
    IIdentityDataProtector dataProtector,
    PasswordHasher passwordHasher,
    IClock clock,
    ICorrelationContext correlationContext) : IStaffAccounts, IStaffDirectory
{
    public async Task<Result<StaffProfile>> AuthenticateAsync(
        StaffLoginInput input,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(input.Email);
        var staff = email is null
            ? null
            : await repository.FindStaffByEmailAsync(
                dataProtector.CreateLookup("staff-email", email),
                correlationContext.TenantId,
                cancellationToken);
        var valid = passwordHasher.Verify(input.Password ?? string.Empty, staff?.PasswordHash);
        if (!valid || staff is null || !staff.IsActive)
        {
            return Result<StaffProfile>.Failure("identity.invalid-credentials", "帳號或密碼錯誤。");
        }

        return ToProfile(staff);
    }

    public async Task<Result<StaffProfile>> GetProfileAsync(
        StaffId staffId,
        CancellationToken cancellationToken)
    {
        var staff = await repository.FindStaffAsync(staffId, correlationContext.TenantId, cancellationToken);
        return staff is null || !staff.IsActive
            ? Result<StaffProfile>.Failure("identity.staff-not-found", "找不到員工。")
            : ToProfile(staff);
    }

    public async Task<Result<StaffProfile>> CreateAsync(
        CreateStaffInput input,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(input.Email);
        var displayName = input.DisplayName?.Trim();
        if (email is null || string.IsNullOrEmpty(displayName) || displayName.Length > 50 ||
            input.Password is not { Length: >= 8 and <= 128 })
        {
            return Result<StaffProfile>.Failure("identity.invalid-staff", "員工資料格式不正確。");
        }

        var lookup = dataProtector.CreateLookup("staff-email", email);
        if (await repository.FindStaffByEmailAsync(lookup, correlationContext.TenantId, cancellationToken) is not null)
        {
            return Result<StaffProfile>.Failure("identity.staff-email-conflict", "此電子郵件已被使用。");
        }

        var staff = StaffAccount.Create(
            StaffId.New(),
            correlationContext.TenantId,
            displayName,
            lookup,
            dataProtector.Protect(email),
            passwordHasher.Hash(input.Password),
            input.Role,
            clock.UtcNow);
        repository.AddStaff(staff);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (IdentityPersistenceConflictException exception)
            when (exception.ConflictCode == "identity.staff-email-conflict")
        {
            return Result<StaffProfile>.Failure(
                "identity.staff-email-conflict",
                "此電子郵件已被使用。");
        }
        return ToProfile(staff);
    }

    public async Task<Result<StaffRole>> GetRoleAsync(StaffId id, CancellationToken cancellationToken)
    {
        var result = await GetProfileAsync(id, cancellationToken);
        return result.IsSuccess
            ? result.Value.Role
            : Result<StaffRole>.Failure(result.Error);
    }

    private StaffProfile ToProfile(StaffAccount staff) => new(
        staff.Id,
        staff.DisplayName,
        dataProtector.Unprotect(staff.EncryptedEmail),
        staff.Role);

    private static string? NormalizeEmail(string? value)
    {
        var normalized = value?.Trim();
        return !string.IsNullOrEmpty(normalized) && normalized.Length <= 254 &&
               MailAddress.TryCreate(normalized, out _)
            ? normalized.ToLowerInvariant()
            : null;
    }
}

internal sealed class StaffRolePolicy : IStaffRolePolicy
{
    public bool Allows(StaffRole actualRole, StaffRole requiredRole) =>
        actualRole == StaffRole.Owner ||
        actualRole == requiredRole ||
        requiredRole == StaffRole.ReadOnly;
}
