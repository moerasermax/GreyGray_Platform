using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GreyGray.Modules.Ordering.Infra;

internal sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        MarkParentsOfChangedLinesForVersionCheck();

        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            ChangeTracker.Clear();
            throw new OrderingConcurrencyException(
                "訂單已被其他操作更新。",
                exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgres
                && IsCheckoutAlreadyPlaced(postgres))
        {
            ChangeTracker.Clear();
            throw new OrderingCheckoutAlreadyPlacedException(
                "同一購物車或結帳事件已由另一個執行者建立訂單。",
                exception);
        }
    }

    internal static bool IsCheckoutAlreadyPlaced(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UniqueViolation
        && exception.ConstraintName is
            "ux_orders_tenant_checkout_cart" or "ux_orders_checkout_event";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("ordering");
        ConfigureOrder(modelBuilder);
        ConfigureOrderLine(modelBuilder);
        modelBuilder.AddPlatformTables();
    }

    private static void ConfigureOrder(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Order>();
        entity.ToTable(
            "orders",
            "ordering",
            table =>
            {
                table.HasCheckConstraint(
                    "orders_source_channel_m0_own_only",
                    "source_channel = 0");
                table.HasCheckConstraint(
                    "ck_orders_totals_non_negative",
                    "goods_total_amount_minor >= 0 AND shipping_fee_amount_minor >= 0 " +
                    "AND grand_total_amount_minor >= 0");
                // 以下三條與 db/migrations/0006_m1a_core.sql:598-615 逐字一致。
                // 補進 EF model 的理由：所有 Ordering 整合測試都用 EnsureCreatedAsync() 建 schema，
                // 少宣告一條，測試資料庫就少守一條——`Order.CapturePayment` 誤設 RefundedCurrency
                // 這個 bug 活過 172 條測試，正是因為 orders_refunded_consistent 只存在於 migration。
                table.HasCheckConstraint(
                    "orders_currency_consistent",
                    "goods_total_currency = shipping_fee_currency " +
                    "AND goods_total_currency = grand_total_currency " +
                    "AND goods_total_currency IN " +
                    "('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD')");
                table.HasCheckConstraint(
                    "ck_orders_paid_non_negative",
                    "paid_amount_minor IS NULL OR paid_amount_minor >= 0");
                table.HasCheckConstraint(
                    "orders_paid_consistent",
                    "(paid_amount_minor IS NULL AND paid_currency IS NULL) " +
                    "OR (paid_amount_minor IS NOT NULL " +
                    "AND paid_currency IS NOT NULL " +
                    "AND paid_amount_minor >= 0 " +
                    "AND paid_currency = grand_total_currency)");
                table.HasCheckConstraint(
                    "ck_orders_refunded_non_negative",
                    "refunded_amount_minor >= 0");
                table.HasCheckConstraint(
                    "orders_refunded_consistent",
                    "(refunded_amount_minor = 0 AND refunded_currency IS NULL) " +
                    "OR (refunded_amount_minor > 0 " +
                    "AND refunded_currency IS NOT NULL " +
                    "AND refunded_currency = grand_total_currency)");
            });
        entity.HasKey(order => order.Id);
        entity.Property(order => order.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OrderId(value))
            .ValueGeneratedNever();
        entity.Property<uint>("xmin")
            .IsRowVersion();
        entity.Property(order => order.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasDefaultValue(TenantId.Default)
            .IsRequired();
        entity.Property(order => order.CheckoutEventId)
            .HasColumnName("checkout_event_id")
            .IsRequired();
        entity.Property(order => order.CheckoutCartId)
            .HasColumnName("checkout_cart_id")
            .HasConversion(id => id.Value, value => new CartId(value))
            .IsRequired();
        entity.Property(order => order.CheckoutIdempotencyKey)
            .HasColumnName("checkout_idempotency_key")
            .HasMaxLength(255)
            .IsRequired();
        entity.Property(order => order.OrderNumber)
            .HasColumnName("order_number")
            .HasMaxLength(32)
            .IsRequired();
        entity.Property(order => order.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(id => id.Value, value => new CustomerId(value))
            .IsRequired();
        entity.Property(order => order.Source)
            .HasColumnName("source_channel")
            .HasConversion<short>()
            .HasDefaultValue(SourceChannel.Own)
            .IsRequired();
        entity.Property(order => order.Status)
            .HasColumnName("status")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(order => order.ShippingPolicy)
            .HasColumnName("shipping_policy")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(order => order.DeliveryMethod)
            .HasColumnName("delivery_method")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(order => order.ShippingAddressId)
            .HasColumnName("shipping_address_id")
            .HasConversion(
                id => id!.Value.Value,
                value => new AddressId(value));
        entity.Property(order => order.ConvenienceStoreCode)
            .HasColumnName("convenience_store_code")
            .HasMaxLength(50);
        entity.Property(order => order.ConvenienceStoreName)
            .HasColumnName("convenience_store_name")
            .HasMaxLength(50);
        entity.Property(order => order.ConvenienceStoreAddress)
            .HasColumnName("convenience_store_address")
            .HasMaxLength(200);
        entity.Property(order => order.RecipientName)
            .HasColumnName("recipient_name")
            .HasMaxLength(50);
        entity.Property(order => order.RecipientPhone)
            .HasColumnName("recipient_phone")
            .HasMaxLength(20);
        entity.Property(order => order.RecipientAddress)
            .HasColumnName("recipient_address")
            .HasMaxLength(200);
        entity.Property(order => order.BuyerNote)
            .HasColumnName("buyer_note")
            .HasMaxLength(200);
        entity.Property(order => order.PricingSnapshotId)
            .HasColumnName("pricing_snapshot_id")
            .HasConversion(id => id.Value, value => new PricingSnapshotId(value))
            .IsRequired();
        entity.Property(order => order.GoodsTotalAmountMinor)
            .HasColumnName("goods_total_amount_minor")
            .IsRequired();
        entity.Property(order => order.GoodsTotalCurrency)
            .HasColumnName("goods_total_currency")
            .HasConversion<string>()
            .HasMaxLength(3)
            .IsRequired();
        entity.Property(order => order.ShippingFeeAmountMinor)
            .HasColumnName("shipping_fee_amount_minor")
            .IsRequired();
        entity.Property(order => order.ShippingFeeCurrency)
            .HasColumnName("shipping_fee_currency")
            .HasConversion<string>()
            .HasMaxLength(3)
            .IsRequired();
        entity.Property(order => order.GrandTotalAmountMinor)
            .HasColumnName("grand_total_amount_minor")
            .IsRequired();
        entity.Property(order => order.GrandTotalCurrency)
            .HasColumnName("grand_total_currency")
            .HasConversion<string>()
            .HasMaxLength(3)
            .IsRequired();
        entity.Property(order => order.PaidAmountMinor)
            .HasColumnName("paid_amount_minor");
        entity.Property(order => order.PaidCurrency)
            .HasColumnName("paid_currency")
            .HasConversion<string>()
            .HasMaxLength(3);
        entity.Property(order => order.RefundedAmountMinor)
            .HasColumnName("refunded_amount_minor")
            .HasDefaultValue(0L)
            .IsRequired();
        entity.Property(order => order.RefundedCurrency)
            .HasColumnName("refunded_currency")
            .HasConversion<string>()
            .HasMaxLength(3);
        entity.Property(order => order.QuoteExplainJson)
            .HasColumnName("quote_explain")
            .HasColumnType("jsonb")
            .IsRequired();
        entity.Property(order => order.PlacedAt)
            .HasColumnName("placed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.Property(order => order.PaymentDueAt)
            .HasColumnName("payment_due_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(order => order.PaymentAutoCancelAt)
            .HasColumnName("payment_auto_cancel_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(order => order.AppraisalDueAt)
            .HasColumnName("appraisal_due_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(order => order.CancelledAt)
            .HasColumnName("cancelled_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(order => order.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(200);
        entity.Property(order => order.CancellationSource)
            .HasColumnName("cancellation_source")
            .HasConversion<short>();
        entity.Property(order => order.LastPaymentFailureCode)
            .HasColumnName("last_payment_failure_code")
            .HasMaxLength(100);

        entity.Ignore(order => order.GoodsTotal);
        entity.Ignore(order => order.ShippingFee);
        entity.Ignore(order => order.GrandTotal);
        entity.Ignore(order => order.PaidAmount);
        entity.Ignore(order => order.RefundedAmount);

        entity.HasMany(order => order.Lines)
            .WithOne()
            .HasForeignKey(line => line.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(order => order.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        entity.HasIndex(order => new { order.TenantId, order.CheckoutCartId })
            .IsUnique()
            .HasDatabaseName("ux_orders_tenant_checkout_cart");
        entity.HasIndex(order => order.CheckoutEventId)
            .IsUnique()
            .HasDatabaseName("ux_orders_checkout_event");
        entity.HasIndex(order => new { order.TenantId, order.CheckoutIdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_orders_tenant_checkout_key");
        entity.HasIndex(order => new { order.TenantId, order.OrderNumber })
            .IsUnique()
            .HasDatabaseName("ux_orders_tenant_number");
        entity.HasIndex(order => new { order.TenantId, order.CustomerId, order.PlacedAt })
            .HasDatabaseName("ix_orders_tenant_customer_placed");
        entity.HasIndex(order => new { order.TenantId, order.Status, order.PlacedAt })
            .HasDatabaseName("ix_orders_tenant_status_placed");
    }

    private static void ConfigureOrderLine(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OrderLine>();
        entity.ToTable(
            "order_line",
            "ordering",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_order_line_quantity",
                    "quantity BETWEEN 1 AND 999");
                // 與 db/migrations/0015_ordering_partial_purchase_shortfall.sql 逐字一致。
                table.HasCheckConstraint(
                    "ck_order_line_quantity_shortfall",
                    "quantity_shortfall BETWEEN 0 AND 999");
                table.HasCheckConstraint(
                    "ck_order_line_unit_price",
                    "unit_price_amount_minor >= 0");
            });
        entity.HasKey(line => line.Id);
        entity.Property(line => line.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OrderLineId(value))
            .ValueGeneratedNever();
        entity.Property(line => line.OrderId)
            .HasColumnName("order_id")
            .HasConversion(id => id.Value, value => new OrderId(value))
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
        entity.Property(line => line.Mode)
            .HasColumnName("fulfillment_mode")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(line => line.Status)
            .HasColumnName("status")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(line => line.Quantity)
            .HasColumnName("quantity")
            .IsRequired();
        entity.Property(line => line.QuantityShortfall)
            .HasColumnName("quantity_shortfall")
            .IsRequired();
        entity.Property(line => line.UnitPriceAmountMinor)
            .HasColumnName("unit_price_amount_minor")
            .IsRequired();
        entity.Property(line => line.UnitPriceCurrency)
            .HasColumnName("unit_price_currency")
            .HasConversion<string>()
            .HasMaxLength(3)
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
        entity.Property(line => line.ConsumedLotId)
            .HasColumnName("consumed_lot_id")
            .HasConversion(
                id => id!.Value.Value,
                value => new LotId(value));
        entity.Property(line => line.RefundedAmountMinor)
            .HasColumnName("refunded_amount_minor");
        entity.Property(line => line.RefundedCurrency)
            .HasColumnName("refunded_currency")
            .HasConversion<string>()
            .HasMaxLength(3);
        entity.Property(line => line.GoodsReceivedAt)
            .HasColumnName("goods_received_at")
            .HasColumnType("timestamp with time zone");
        entity.Ignore(line => line.UnitPrice);

        entity.HasIndex(line => new { line.TenantId, line.OrderId })
            .HasDatabaseName("ix_order_line_tenant_order");
        entity.HasIndex(line => new { line.TenantId, line.CampaignId, line.OrderId })
            .HasDatabaseName("ix_order_line_tenant_campaign_order");
    }

    private void MarkParentsOfChangedLinesForVersionCheck()
    {
        var changedOrderIds = ChangeTracker.Entries<OrderLine>()
            .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Entity.OrderId)
            .ToHashSet();

        if (changedOrderIds.Count == 0)
        {
            return;
        }

        foreach (var orderEntry in ChangeTracker.Entries<Order>())
        {
            if (orderEntry.State == EntityState.Unchanged
                && changedOrderIds.Contains(orderEntry.Entity.Id))
            {
                orderEntry.Property(order => order.PlacedAt).IsModified = true;
            }
        }
    }
}
