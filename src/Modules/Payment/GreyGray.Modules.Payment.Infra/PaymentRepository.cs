using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Payment.Core;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using PaymentEntity = GreyGray.Modules.Payment.Core.Payment;

namespace GreyGray.Modules.Payment.Infra;

internal sealed class PaymentRepository(PaymentDbContext dbContext) : IPaymentRepository
{
    public void Add(PaymentEntity payment) => dbContext.Payments.Add(payment);

    public Task<PaymentEntity?> FindByOrderAsync(
        TenantId tenantId,
        OrderId orderId,
        CancellationToken cancellationToken) =>
        dbContext.Payments
            .Where(payment => payment.TenantId == tenantId && payment.OrderId == orderId)
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PaymentEntity?> FindByMerchantTradeNoAsync(
        TenantId tenantId,
        string merchantTradeNo,
        CancellationToken cancellationToken) =>
        dbContext.Payments.SingleOrDefaultAsync(
            payment => payment.TenantId == tenantId
                && payment.MerchantTradeNo == merchantTradeNo,
            cancellationToken);

    public Task<PaymentEntity?> FindByIdAsync(
        TenantId tenantId,
        PaymentId id,
        CancellationToken cancellationToken) =>
        dbContext.Payments.SingleOrDefaultAsync(
            payment => payment.TenantId == tenantId && payment.Id == id,
            cancellationToken);

    public async Task<IReadOnlyList<PaymentEntity>> FindByOrderAllAsync(
        TenantId tenantId,
        OrderId orderId,
        CancellationToken cancellationToken) =>
        await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.TenantId == tenantId && payment.OrderId == orderId)
            .OrderByDescending(payment => payment.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public Task<PaymentEntity?> FindCapturedOrRefundedByOrderAsync(
        TenantId tenantId,
        OrderId orderId,
        CancellationToken cancellationToken) =>
        dbContext.Payments
            .Where(payment => payment.TenantId == tenantId &&
                              payment.OrderId == orderId &&
                              (payment.Status == PaymentStatus.Captured ||
                               payment.Status == PaymentStatus.PartiallyRefunded ||
                               payment.Status == PaymentStatus.Refunded))
            .OrderByDescending(payment => payment.CapturedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
