using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Pricing.Infra;

internal sealed class PricingDbContext(DbContextOptions<PricingDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<PricingSnapshotEntity> PricingSnapshots => Set<PricingSnapshotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("pricing");
        ConfigureSnapshot(modelBuilder);
        modelBuilder.AddPlatformTables();
    }

    private static void ConfigureSnapshot(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PricingSnapshotEntity>();
        entity.ToTable("pricing_snapshot", "pricing");
        entity.HasKey(snapshot => snapshot.Id);
        entity.Property(snapshot => snapshot.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new PricingSnapshotId(value))
            .ValueGeneratedNever();
        entity.Property(snapshot => snapshot.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();
        entity.Property(snapshot => snapshot.DeliveryMethod)
            .HasColumnName("delivery_method")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(snapshot => snapshot.ActualWeightGram)
            .HasColumnName("actual_weight_gram")
            .IsRequired();
        entity.Property(snapshot => snapshot.VolumetricWeightGram)
            .HasColumnName("volumetric_weight_gram")
            .IsRequired();
        entity.Property(snapshot => snapshot.BillableWeightGram)
            .HasColumnName("billable_weight_gram")
            .IsRequired();
        entity.Property(snapshot => snapshot.ShippingFeeAmountMinor)
            .HasColumnName("shipping_fee_minor")
            .IsRequired();
        entity.Property(snapshot => snapshot.ShippingFeeCurrency)
            .HasColumnName("shipping_fee_currency")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(snapshot => snapshot.AppliedRuleSetId)
            .HasColumnName("applied_rule_set_id")
            .HasConversion(id => id.Value, value => new FeeRuleSetId(value))
            .IsRequired();
        entity.Property(snapshot => snapshot.AppliedRuleId)
            .HasColumnName("applied_rule_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new FeeRuleId(value.Value) : null);
        entity.Property(snapshot => snapshot.AppliedStrategy)
            .HasColumnName("applied_strategy")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(snapshot => snapshot.Explain)
            .HasColumnName("explain")
            .HasColumnType("jsonb")
            .IsRequired();
        entity.Property(snapshot => snapshot.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.HasIndex(snapshot => new { snapshot.TenantId, snapshot.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_pricing_snapshot_tenant_created");
    }
}

internal sealed class PricingSnapshotEntity
{
    private PricingSnapshotEntity()
    {
    }

    private PricingSnapshotEntity(TenantId tenantId, PricingSnapshot snapshot)
    {
        Id = snapshot.Id;
        TenantId = tenantId;
        DeliveryMethod = snapshot.DeliveryMethod;
        ActualWeightGram = snapshot.ActualWeightGram;
        VolumetricWeightGram = snapshot.VolumetricWeightGram;
        BillableWeightGram = snapshot.BillableWeightGram;
        ShippingFeeAmountMinor = snapshot.ShippingFee.AmountMinor;
        ShippingFeeCurrency = snapshot.ShippingFee.Currency;
        AppliedRuleSetId = snapshot.AppliedRuleSetId;
        AppliedRuleId = snapshot.AppliedRuleId;
        AppliedStrategy = snapshot.AppliedStrategy;
        Explain = snapshot.Explain.ToArray();
        CreatedAt = snapshot.CreatedAt;
    }

    public PricingSnapshotId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public DeliveryMethod DeliveryMethod { get; private set; }

    public int ActualWeightGram { get; private set; }

    public int VolumetricWeightGram { get; private set; }

    public int BillableWeightGram { get; private set; }

    public long ShippingFeeAmountMinor { get; private set; }

    public Currency ShippingFeeCurrency { get; private set; }

    public FeeRuleSetId AppliedRuleSetId { get; private set; }

    public FeeRuleId? AppliedRuleId { get; private set; }

    public ShippingStrategyKind AppliedStrategy { get; private set; }

    public string[] Explain { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public static PricingSnapshotEntity From(TenantId tenantId, PricingSnapshot snapshot) =>
        new(tenantId, snapshot);

    public PricingSnapshot ToContract() =>
        new(
            Id,
            DeliveryMethod,
            ActualWeightGram,
            VolumetricWeightGram,
            BillableWeightGram,
            new Money(ShippingFeeAmountMinor, ShippingFeeCurrency),
            AppliedRuleSetId,
            AppliedRuleId,
            AppliedStrategy,
            Explain,
            CreatedAt);
}
