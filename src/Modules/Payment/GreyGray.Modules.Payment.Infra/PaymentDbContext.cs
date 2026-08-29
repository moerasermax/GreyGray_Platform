using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Payment.Core;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Platform;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using PaymentEntity = GreyGray.Modules.Payment.Core.Payment;

namespace GreyGray.Modules.Payment.Infra;

internal sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("payment");

        var payment = modelBuilder.Entity<PaymentEntity>();
        payment.ToTable("payment", "payment");
        payment.HasKey(value => value.Id);
        payment.Property(value => value.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new PaymentId(value))
            .ValueGeneratedNever();
        payment.Property(value => value.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();
        payment.Property(value => value.OrderId)
            .HasColumnName("order_id")
            .HasConversion(id => id.Value, value => new OrderId(value))
            .IsRequired();
        payment.Property(value => value.Provider)
            .HasColumnName("provider")
            .HasConversion<short>()
            .IsRequired();
        payment.Property(value => value.Status)
            .HasColumnName("status")
            .HasConversion<short>()
            .IsConcurrencyToken()
            .IsRequired();
        payment.Property(value => value.GoodsAmountMinor)
            .HasColumnName("goods_amount_minor")
            .IsRequired();
        payment.Property(value => value.ShippingAmountMinor)
            .HasColumnName("shipping_amount_minor")
            .IsRequired();
        payment.Property(value => value.FeeAmountMinor)
            .HasColumnName("fee_amount_minor");
        payment.Property(value => value.RefundedAmountMinor)
            .HasColumnName("refunded_amount_minor")
            .HasDefaultValue(0L)
            .IsRequired();
        payment.Ignore(value => value.Amount);
        payment.Ignore(value => value.GoodsAmount);
        payment.Ignore(value => value.ShippingAmount);
        payment.Ignore(value => value.Fee);
        payment.Ignore(value => value.RefundedAmount);
        payment.Property(value => value.MerchantTradeNo)
            .HasColumnName("merchant_trade_no")
            .HasMaxLength(20)
            .IsRequired();
        payment.Property(value => value.ProviderTransactionId)
            .HasColumnName("provider_transaction_id")
            .HasMaxLength(32);
        payment.Property(value => value.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        payment.Property(value => value.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        payment.Property(value => value.CapturedAt)
            .HasColumnName("captured_at")
            .HasColumnType("timestamp with time zone");
        payment.Property(value => value.SettledAt)
            .HasColumnName("settled_at")
            .HasColumnType("timestamp with time zone");
        payment.HasIndex(value => value.MerchantTradeNo)
            .IsUnique()
            .HasDatabaseName("ux_payment_merchant_trade_no");
        payment.HasIndex(value => new { value.TenantId, value.OrderId })
            .IsUnique()
            .HasFilter("status IN (0, 1, 4)")
            .HasDatabaseName("ux_payment_tenant_order_active");

        modelBuilder.AddPlatformTables();
    }
}
