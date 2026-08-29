using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Campaign.Core;
using GreyGray.Platform;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Campaign.Infra;

internal sealed class CampaignDbContext(DbContextOptions<CampaignDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<CampaignAggregate> Campaigns => Set<CampaignAggregate>();

    public DbSet<CampaignOfferEntity> Offers => Set<CampaignOfferEntity>();

    public DbSet<TripCostEntity> TripCosts => Set<TripCostEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("campaign");
        ConfigureCampaign(modelBuilder);
        ConfigureOffer(modelBuilder);
        ConfigureTripCost(modelBuilder);
        modelBuilder.AddPlatformTables();
    }

    private static void ConfigureCampaign(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CampaignAggregate>();
        entity.ToTable("campaign", "campaign");
        entity.HasKey(campaign => campaign.Id);
        entity.Property(campaign => campaign.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CampaignId(value))
            .ValueGeneratedNever();
        entity.Property(campaign => campaign.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();
        entity.Property(campaign => campaign.Title)
            .HasColumnName("title")
            .HasMaxLength(100)
            .IsRequired();
        entity.Property(campaign => campaign.Destination)
            .HasColumnName("destination")
            .HasMaxLength(50)
            .IsRequired();
        entity.Property(campaign => campaign.DepartAt)
            .HasColumnName("depart_at")
            .HasColumnType("date")
            .IsRequired();
        entity.Property(campaign => campaign.ReturnAt)
            .HasColumnName("return_at")
            .HasColumnType("date")
            .IsRequired();
        entity.Property(campaign => campaign.ClosesAt)
            .HasColumnName("closes_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.Property(campaign => campaign.Status)
            .HasColumnName("status")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(campaign => campaign.Description)
            .HasColumnName("description");
        entity.Property(campaign => campaign.CoverImageUrl)
            .HasColumnName("cover_image_url");
        entity.Property(campaign => campaign.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.Property(campaign => campaign.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.Property<uint>("xmin").IsRowVersion();
        entity.HasAlternateKey(campaign => new { campaign.TenantId, campaign.Id });
        entity.HasIndex(campaign => new
            {
                campaign.TenantId,
                campaign.Status,
                campaign.CreatedAt,
            })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_campaign_tenant_status_created");
        entity.HasMany(campaign => campaign.Offers)
            .WithOne()
            .HasForeignKey(offer => offer.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(campaign => campaign.Offers)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        entity.HasMany(campaign => campaign.TripCosts)
            .WithOne()
            .HasForeignKey(cost => new { cost.TenantId, cost.CampaignId })
            .HasPrincipalKey(campaign => new { campaign.TenantId, campaign.Id })
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(campaign => campaign.TripCosts)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureOffer(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CampaignOfferEntity>();
        entity.ToTable("campaign_offer", "campaign");
        entity.HasKey(offer => offer.Id);
        entity.Property(offer => offer.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CampaignOfferId(value))
            .ValueGeneratedNever();
        entity.Property(offer => offer.CampaignId)
            .HasColumnName("campaign_id")
            .HasConversion(id => id.Value, value => new CampaignId(value))
            .IsRequired();
        entity.Property(offer => offer.SkuId)
            .HasColumnName("sku_id")
            .HasConversion(id => id.Value, value => new GreyGray.Modules.Catalog.Contracts.SkuId(value))
            .IsRequired();
        entity.Property(offer => offer.SellingPriceAmountMinor)
            .HasColumnName("selling_price_minor")
            .IsRequired();
        entity.Property(offer => offer.SellingPriceCurrency)
            .HasColumnName("selling_price_currency")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(offer => offer.TargetPurchasePriceAmountMinor)
            .HasColumnName("target_purchase_price_minor");
        entity.Property(offer => offer.TargetPurchasePriceCurrency)
            .HasColumnName("target_purchase_price_currency")
            .HasConversion<short?>();
        entity.Property(offer => offer.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        entity.Property(offer => offer.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.Ignore(offer => offer.SellingPrice);
        entity.Ignore(offer => offer.TargetPurchasePrice);
        entity.HasIndex(offer => new { offer.CampaignId, offer.SkuId })
            .IsUnique()
            .HasDatabaseName("uq_campaign_offer_campaign_sku");
    }

    private static void ConfigureTripCost(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TripCostEntity>();
        entity.ToTable(
            "trip_cost",
            "campaign",
            table => table.HasCheckConstraint(
                "ck_trip_cost_amount_non_negative",
                "amount_minor >= 0"));
        entity.HasKey(cost => cost.Id);
        entity.Property(cost => cost.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new TripCostId(value))
            .ValueGeneratedNever();
        entity.Property(cost => cost.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();
        entity.Property(cost => cost.CampaignId)
            .HasColumnName("campaign_id")
            .HasConversion(id => id.Value, value => new CampaignId(value))
            .IsRequired();
        entity.Property(cost => cost.Kind)
            .HasColumnName("kind")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(cost => cost.AmountMinor)
            .HasColumnName("amount_minor")
            .IsRequired();
        entity.Property(cost => cost.Currency)
            .HasColumnName("currency")
            .HasConversion<string>()
            .HasMaxLength(3)
            .IsRequired();
        entity.Property(cost => cost.Memo)
            .HasColumnName("memo")
            .HasMaxLength(200)
            .IsRequired();
        entity.Property(cost => cost.RecordedAt)
            .HasColumnName("recorded_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.Ignore(cost => cost.Amount);
        entity.HasIndex(cost => new { cost.TenantId, cost.Id })
            .IsUnique()
            .HasDatabaseName("uq_trip_cost_tenant_id");
        entity.HasIndex(cost => new { cost.TenantId, cost.CampaignId, cost.RecordedAt })
            .HasDatabaseName("ix_trip_cost_tenant_campaign_recorded");
    }
}
