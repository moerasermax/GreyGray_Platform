using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Fulfillment.Core;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Fulfillment.Infra;

internal sealed class FulfillmentDbContext(DbContextOptions<FulfillmentDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<ShipmentAggregate> Shipments => Set<ShipmentAggregate>();

    public DbSet<ShipmentOrderLink> ShipmentOrderLinks => Set<ShipmentOrderLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("fulfillment");
        ConfigureShipment(modelBuilder);
        ConfigureShipmentOrderLink(modelBuilder);
        modelBuilder.AddPlatformTables();
    }

    private static void ConfigureShipment(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ShipmentAggregate>();
        entity.ToTable(
            "shipment",
            "fulfillment",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_shipment_carrier_cost_non_negative",
                    "carrier_cost_amount_minor IS NULL OR carrier_cost_amount_minor >= 0");
                table.HasCheckConstraint(
                    "shipment_carrier_cost_complete",
                    "(carrier_cost_amount_minor IS NULL AND carrier_cost_currency IS NULL) " +
                    "OR (carrier_cost_amount_minor IS NOT NULL " +
                    "AND carrier_cost_currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD'))");
            });

        entity.HasKey(shipment => shipment.Id);
        entity.Property(shipment => shipment.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ShipmentId(value))
            .ValueGeneratedNever();
        entity.Property(shipment => shipment.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasDefaultValue(TenantId.Default)
            .IsRequired();
        entity.Property(shipment => shipment.Method)
            .HasColumnName("method")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(shipment => shipment.Status)
            .HasColumnName("status")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(shipment => shipment.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(100);
        entity.Property(shipment => shipment.CarrierCostAmountMinor)
            .HasColumnName("carrier_cost_amount_minor");
        entity.Property(shipment => shipment.CarrierCostCurrency)
            .HasColumnName("carrier_cost_currency")
            .HasConversion<string>()
            .HasMaxLength(3);
        entity.Property(shipment => shipment.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.Property(shipment => shipment.DispatchedAt)
            .HasColumnName("dispatched_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(shipment => shipment.DeliveredAt)
            .HasColumnName("delivered_at")
            .HasColumnType("timestamp with time zone");

        entity.Ignore(shipment => shipment.CarrierCost);
        entity.Ignore(shipment => shipment.OrderIds);

        entity.HasMany(shipment => shipment.OrderLinks)
            .WithOne()
            .HasForeignKey(link => link.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(shipment => shipment.OrderLinks)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        entity.HasIndex(shipment => new { shipment.TenantId, shipment.Status, shipment.CreatedAt })
            .HasDatabaseName("ix_shipment_tenant_status_created");
    }

    private static void ConfigureShipmentOrderLink(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ShipmentOrderLink>();
        entity.ToTable("shipment_order", "fulfillment");

        entity.HasKey(link => link.Id);
        entity.Property(link => link.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        entity.Property(link => link.ShipmentId)
            .HasColumnName("shipment_id")
            .HasConversion(id => id.Value, value => new ShipmentId(value))
            .IsRequired();
        entity.Property(link => link.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasDefaultValue(TenantId.Default)
            .IsRequired();
        entity.Property(link => link.OrderId)
            .HasColumnName("order_id")
            .HasConversion(id => id.Value, value => new OrderId(value))
            .IsRequired();

        entity.HasIndex(link => new { link.TenantId, link.ShipmentId, link.OrderId })
            .IsUnique()
            .HasDatabaseName("ux_shipment_order_tenant_shipment_order");
        entity.HasIndex(link => new { link.TenantId, link.OrderId })
            .HasDatabaseName("ix_shipment_order_tenant_order");
    }
}
