using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Checkout.Core;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Checkout.Infra;

internal sealed class CheckoutDbContext(DbContextOptions<CheckoutDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<CartLineEntity> CartLines => Set<CartLineEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("checkout");
        ConfigureCart(modelBuilder);
        ConfigureCartLine(modelBuilder);
        modelBuilder.AddPlatformTables();
    }

    private static void ConfigureCart(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Cart>();
        entity.ToTable("cart", "checkout");
        entity.HasKey(cart => cart.Id);
        entity.Property(cart => cart.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CartId(value))
            .ValueGeneratedNever();
        entity.Property(cart => cart.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasDefaultValue(TenantId.Default)
            .IsRequired();
        entity.Property(cart => cart.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(
                id => id!.Value.Value,
                value => new CustomerId(value));
        entity.Property(cart => cart.ShippingPolicy)
            .HasColumnName("shipping_policy")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(cart => cart.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.Property(cart => cart.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.Property(cart => cart.CompletedEventId)
            .HasColumnName("completed_event_id");
        entity.Property(cart => cart.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(cart => cart.CheckoutIdempotencyKey)
            .HasColumnName("checkout_idempotency_key")
            .HasMaxLength(255);
        entity.Property(cart => cart.CompletedPricingSnapshotId)
            .HasColumnName("pricing_snapshot_id")
            .HasConversion(
                id => id!.Value.Value,
                value => new PricingSnapshotId(value));
        entity.Property(cart => cart.CompletedDeliveryMethod)
            .HasColumnName("delivery_method")
            .HasConversion<short?>();
        entity.Property(cart => cart.CompletedShippingAddressId)
            .HasColumnName("shipping_address_id")
            .HasConversion(
                id => id!.Value.Value,
                value => new AddressId(value));
        entity.Property(cart => cart.CompletedConvenienceStoreCode)
            .HasColumnName("convenience_store_code")
            .HasMaxLength(50);
        entity.Property(cart => cart.CompletedConvenienceStoreName)
            .HasColumnName("convenience_store_name")
            .HasMaxLength(50);
        entity.Property(cart => cart.CompletedConvenienceStoreAddress)
            .HasColumnName("convenience_store_address")
            .HasMaxLength(200);
        entity.Property(cart => cart.CompletedBuyerNote)
            .HasColumnName("buyer_note")
            .HasMaxLength(200);

        entity.HasMany(cart => cart.Lines)
            .WithOne()
            .HasForeignKey(line => line.CartId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(cart => cart.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        entity.HasIndex(cart => new { cart.TenantId, cart.CustomerId, cart.UpdatedAt })
            .HasDatabaseName("ix_cart_tenant_customer_updated");
        entity.HasIndex(cart => new { cart.TenantId, cart.CheckoutIdempotencyKey })
            .IsUnique()
            .HasFilter("checkout_idempotency_key IS NOT NULL")
            .HasDatabaseName("ux_cart_tenant_checkout_key");
        entity.HasIndex(cart => cart.CompletedEventId)
            .IsUnique()
            .HasFilter("completed_event_id IS NOT NULL")
            .HasDatabaseName("ux_cart_completed_event");
    }

    private static void ConfigureCartLine(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CartLineEntity>();
        entity.ToTable(
            "cart_line",
            "checkout",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_cart_line_quantity",
                    "quantity BETWEEN 1 AND 999");
                table.HasCheckConstraint(
                    "ck_cart_line_unit_price",
                    "unit_price_amount_minor >= 0");
            });
        entity.HasKey(line => line.Id);
        entity.Property(line => line.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CartLineId(value))
            .ValueGeneratedNever();
        entity.Property(line => line.CartId)
            .HasColumnName("cart_id")
            .HasConversion(id => id.Value, value => new CartId(value))
            .IsRequired();
        entity.Property(line => line.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasDefaultValue(TenantId.Default)
            .IsRequired();
        entity.Property(line => line.SkuId)
            .HasColumnName("sku_id")
            .HasConversion(id => id.Value, value => new SkuId(value))
            .IsRequired();
        entity.Property(line => line.ProductId)
            .HasColumnName("product_id")
            .HasConversion(id => id.Value, value => new ProductId(value))
            .IsRequired();
        entity.Property(line => line.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();
        entity.Property(line => line.VariantName)
            .HasColumnName("variant_name")
            .HasMaxLength(50);
        entity.Property(line => line.Mode)
            .HasColumnName("fulfillment_mode")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(line => line.CampaignId)
            .HasColumnName("campaign_id")
            .HasConversion(
                id => id!.Value.Value,
                value => new CampaignId(value));
        entity.Property(line => line.CampaignOfferId)
            .HasColumnName("campaign_offer_id")
            .HasConversion(
                id => id!.Value.Value,
                value => new CampaignOfferId(value));
        entity.Property(line => line.Quantity)
            .HasColumnName("quantity")
            .IsRequired();
        entity.Property(line => line.UnitPriceAmountMinor)
            .HasColumnName("unit_price_amount_minor")
            .IsRequired();
        entity.Property(line => line.UnitPriceCurrency)
            .HasColumnName("unit_price_currency")
            .HasConversion<string>()
            .HasMaxLength(3)
            .IsRequired();
        entity.Ignore(line => line.UnitPrice);

        entity.HasIndex(line => new { line.TenantId, line.CartId })
            .HasDatabaseName("ix_cart_line_tenant_cart");
        entity.HasIndex(line => new
            {
                line.TenantId,
                line.CartId,
                line.SkuId,
                line.Mode,
                line.CampaignOfferId,
            })
            .IsUnique()
            .HasDatabaseName("ux_cart_line_identity");
    }
}
