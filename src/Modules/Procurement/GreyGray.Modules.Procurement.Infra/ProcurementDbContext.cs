using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Modules.Procurement.Core;
using GreyGray.Platform;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Procurement.Infra;

internal sealed class ProcurementDbContext(DbContextOptions<ProcurementDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<PurchaseItemAggregate> PurchaseItems => Set<PurchaseItemAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("procurement");
        ConfigurePurchaseItem(modelBuilder);
        modelBuilder.AddPlatformTables();
    }

    private static void ConfigurePurchaseItem(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PurchaseItemAggregate>();
        entity.ToTable(
            "purchase_item",
            "procurement",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_purchase_item_quantity_requested",
                    "quantity_requested BETWEEN 1 AND 999");
                table.HasCheckConstraint(
                    "ck_purchase_item_quantity_purchased",
                    "quantity_purchased BETWEEN 0 AND quantity_requested");
                table.HasCheckConstraint(
                    "ck_purchase_item_target_price_non_negative",
                    "target_price_amount_minor IS NULL OR target_price_amount_minor >= 0");
                table.HasCheckConstraint(
                    "purchase_item_target_price_complete",
                    "(target_price_amount_minor IS NULL AND target_price_currency IS NULL) " +
                    "OR (target_price_amount_minor IS NOT NULL " +
                    "AND target_price_currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD'))");
                table.HasCheckConstraint(
                    "ck_purchase_item_actual_paid_complete",
                    "(actual_paid_original_amount_minor IS NULL " +
                    "AND actual_paid_original_currency IS NULL " +
                    "AND actual_paid_booking_amount_minor IS NULL " +
                    "AND actual_paid_booking_currency IS NULL " +
                    "AND actual_paid_fx_snapshot_id IS NULL) " +
                    "OR (actual_paid_original_amount_minor >= 0 " +
                    "AND actual_paid_original_currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD') " +
                    "AND actual_paid_booking_amount_minor >= 0 " +
                    "AND actual_paid_booking_currency = 'TWD')");
            });

        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new PurchaseItemId(value))
            .ValueGeneratedNever();
        entity.Property(item => item.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasDefaultValue(TenantId.Default)
            .IsRequired();
        entity.Property(item => item.CampaignId)
            .HasColumnName("campaign_id")
            .HasConversion(id => id.Value, value => new CampaignId(value))
            .IsRequired();
        entity.Property(item => item.SkuId)
            .HasColumnName("sku_id")
            .HasConversion(id => id.Value, value => new SkuId(value))
            .IsRequired();
        entity.Property(item => item.OrderLineId)
            .HasColumnName("order_line_id")
            .HasConversion(id => id.Value, value => new OrderLineId(value))
            .IsRequired();
        entity.Property(item => item.QuantityRequested)
            .HasColumnName("quantity_requested")
            .IsRequired();
        entity.Property(item => item.QuantityPurchased)
            .HasColumnName("quantity_purchased")
            .HasDefaultValue(0)
            .IsRequired();
        entity.Property(item => item.TargetPriceAmountMinor)
            .HasColumnName("target_price_amount_minor");
        entity.Property(item => item.TargetPriceCurrency)
            .HasColumnName("target_price_currency")
            .HasConversion<string>()
            .HasMaxLength(3);
        entity.Property(item => item.ActualPaidOriginalAmountMinor)
            .HasColumnName("actual_paid_original_amount_minor");
        entity.Property(item => item.ActualPaidOriginalCurrency)
            .HasColumnName("actual_paid_original_currency")
            .HasConversion<string>()
            .HasMaxLength(3);
        entity.Property(item => item.ActualPaidBookingAmountMinor)
            .HasColumnName("actual_paid_booking_amount_minor");
        entity.Property(item => item.ActualPaidBookingCurrency)
            .HasColumnName("actual_paid_booking_currency")
            .HasConversion<string>()
            .HasMaxLength(3);
        entity.Property(item => item.ActualPaidFxSnapshotId)
            .HasColumnName("actual_paid_fx_snapshot_id")
            .HasConversion(
                id => id!.Value.Value,
                value => new FxSnapshotId(value));
        entity.Property(item => item.Status)
            .HasColumnName("status")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(item => item.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.Property(item => item.DecidedAt)
            .HasColumnName("decided_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(item => item.ReceivedAt)
            .HasColumnName("received_at")
            .HasColumnType("timestamp with time zone");

        entity.Ignore(item => item.TargetPrice);
        entity.Ignore(item => item.ActualPaid);

        entity.HasIndex(item => new { item.TenantId, item.OrderLineId })
            .IsUnique()
            .HasDatabaseName("ux_purchase_item_tenant_order_line");
        entity.HasIndex(item => new { item.TenantId, item.CampaignId, item.Status })
            .HasDatabaseName("ix_purchase_item_tenant_campaign_status");
    }
}
