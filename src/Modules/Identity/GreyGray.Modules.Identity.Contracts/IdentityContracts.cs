using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Identity.Contracts;

// ── 識別碼 ───────────────────────────────────────────────────────────────
// 一律用內部產生的穩定 ID，不要拿手機號、Email 或外部平台的會員編號當主鍵。
// （通路擴充接縫 #2：外部平台的 ID 對不回來時，同步只能靠人工比對。）

public readonly record struct CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct StaffId(Guid Value)
{
    public static StaffId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct AddressId(Guid Value)
{
    public static AddressId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("N");
}

// ── 列舉 ─────────────────────────────────────────────────────────────────

/// <summary>會員分級。M1a 全部是 <see cref="Standard"/>；分級規則排在 M6+。</summary>
public enum MemberTier
{
    Standard = 0,
    Silver = 1,
    Gold = 2,
}

/// <summary>團隊角色。權限判斷寫在模組內的 Policy，不是 controller 裡的 <c>if role == admin</c>。</summary>
public enum StaffRole
{
    Owner = 0,
    Operator = 1,
    Accountant = 2,
    ReadOnly = 3,
}

// ── 對外 DTO ─────────────────────────────────────────────────────────────

/// <summary>
/// 給其他模組用的客戶摘要。<b>刻意不含個資明文</b>——
/// 身分證字號、地址、電話、生日在 iam schema 內以 AES-256-GCM 應用層加密，
/// 需要時另走 <see cref="ICustomerDirectory.GetContactAsync"/>，且每次存取寫入 audit。
/// </summary>
public sealed record CustomerSummary(
    CustomerId Id,
    string DisplayName,
    MemberTier Tier,
    bool IsActive);

/// <summary>收件與聯絡資訊（個資）。取用會被記錄到 audit。</summary>
public sealed record CustomerContact(
    CustomerId CustomerId,
    string RecipientName,
    string PhoneNumber,
    string? Email,
    string? LineUserId);

public sealed record ShippingAddress(
    AddressId Id,
    CustomerId CustomerId,
    string RecipientName,
    string PhoneNumber,
    string PostalCode,
    string City,
    string District,
    string StreetAddress);

// ── 同步契約（跨模組唯一允許的直接呼叫面）─────────────────────────────

public interface ICustomerDirectory
{
    Task<Result<CustomerSummary>> GetAsync(CustomerId id, CancellationToken cancellationToken);

    /// <summary>取個資明文。呼叫端必須提供理由，會寫進 audit。</summary>
    Task<Result<CustomerContact>> GetContactAsync(
        CustomerId id,
        StaffId? actor,
        string accessReason,
        CancellationToken cancellationToken);

    Task<Result<ShippingAddress>> GetAddressAsync(AddressId id, CancellationToken cancellationToken);
}

public interface IStaffDirectory
{
    Task<Result<StaffRole>> GetRoleAsync(StaffId id, CancellationToken cancellationToken);
}

// ── 對外事件 ─────────────────────────────────────────────────────────────

public sealed record CustomerRegistered(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    CustomerId CustomerId,
    string DisplayName)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "iam.CustomerRegistered.v1";

    public override string AggregateType => "Customer";

    public override string AggregateId => CustomerId.ToString();
}

public sealed record CustomerDeactivated(
    Guid EventId,
    DateTimeOffset OccurredAt,
    TenantId TenantId,
    CustomerId CustomerId,
    string Reason)
    : IntegrationEventBase(EventId, OccurredAt, TenantId), IIntegrationEvent
{
    public static string EventType => "iam.CustomerDeactivated.v1";

    public override string AggregateType => "Customer";

    public override string AggregateId => CustomerId.ToString();
}
