using GreyGray.Modules.Identity.Contracts;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Identity.Core;

/// <summary>Identity 內部的客戶聚合；明文聯絡資料尚未進入 M0 切片。</summary>
internal sealed class Customer
{
    private Customer()
    {
    }

    private Customer(
        CustomerId id,
        TenantId tenantId,
        string displayName,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        DisplayName = displayName;
        Tier = MemberTier.Standard;
        IsActive = true;
        CreatedAt = createdAt;
    }

    public CustomerId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public MemberTier Tier { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Customer Register(
        CustomerId id,
        TenantId tenantId,
        string displayName,
        DateTimeOffset createdAt) =>
        new(id, tenantId, displayName, createdAt);

    public CustomerSummary ToSummary() => new(Id, DisplayName, Tier, IsActive);
}

internal interface ICustomerRepository
{
    void Add(Customer customer);
}
