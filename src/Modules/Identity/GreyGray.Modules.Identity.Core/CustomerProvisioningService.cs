using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Identity.Core;

/// <summary>建立客戶並在同一個工作單元加入 CustomerRegistered outbox。</summary>
internal sealed class CustomerProvisioningService(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    IEventPublisher eventPublisher,
    IClock clock,
    ICorrelationContext correlationContext) : ICustomerProvisioning
{
    public async Task<Result<CustomerSummary>> CreateAsync(
        string displayName,
        CancellationToken cancellationToken)
    {
        var normalizedName = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result<CustomerSummary>.Failure(
                "identity.display-name-required",
                "請輸入顯示名稱。");
        }

        if (normalizedName.Length > 50)
        {
            return Result<CustomerSummary>.Failure(
                "identity.display-name-too-long",
                "顯示名稱不得超過 50 個字元。");
        }

        var customerId = CustomerId.New();
        var occurredAt = clock.UtcNow;
        var customer = Customer.Register(
            customerId,
            correlationContext.TenantId,
            normalizedName,
            occurredAt);

        customers.Add(customer);
        await eventPublisher.PublishAsync(
            new CustomerRegistered(
                Guid.CreateVersion7(),
                occurredAt,
                correlationContext.TenantId,
                customerId,
                normalizedName),
            cancellationToken);

        // Customer 與 OutboxMessage 都在 IdentityDbContext，一次 SaveChanges 的隱式交易
        // 會保證兩者一起成功或一起失敗。
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return customer.ToSummary();
    }
}
