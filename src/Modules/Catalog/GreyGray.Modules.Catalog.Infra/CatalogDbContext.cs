using GreyGray.Platform;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Catalog.Core;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Catalog.Infra;

/// <summary>Catalog 模組專用的資料庫工作單元。</summary>
internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Sku> Skus => Set<Sku>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("catalog");
        ConfigureCategory(modelBuilder);
        ConfigureProduct(modelBuilder);
        ConfigureProductImage(modelBuilder);
        ConfigureSku(modelBuilder);
        modelBuilder.AddPlatformTables();
    }

    private static void ConfigureCategory(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Category>();
        entity.ToTable("category", "catalog");
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).HasColumnName("id")
            .HasConversion(id => id.Value, value => new CategoryId(value)).ValueGeneratedNever();
        entity.Property(value => value.TenantId).HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value)).IsRequired();
        entity.Property(value => value.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
        entity.Property(value => value.ImageUrl).HasColumnName("image_url").HasMaxLength(2048);
        entity.Property(value => value.SortOrder).HasColumnName("sort_order").IsRequired();
        entity.HasIndex(value => new { value.TenantId, value.SortOrder, value.Name })
            .HasDatabaseName("ix_category_tenant_sort");
    }

    private static void ConfigureProduct(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Product>();
        entity.ToTable("product", "catalog");
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).HasColumnName("id")
            .HasConversion(id => id.Value, value => new ProductId(value)).ValueGeneratedNever();
        entity.Property(value => value.TenantId).HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value)).IsRequired();
        entity.Property(value => value.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        entity.Property(value => value.Description).HasColumnName("description");
        entity.Property(value => value.ShortDescription).HasColumnName("short_description").HasMaxLength(100);
        entity.Property(value => value.CategoryId).HasColumnName("category_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CategoryId(value.Value) : null);
        entity.Property(value => value.Mode).HasColumnName("mode").HasConversion<short>().IsRequired();
        entity.Property(value => value.IsActive).HasColumnName("is_active").IsRequired();
        entity.Property(value => value.CreatedAt).HasColumnName("created_at")
            .HasColumnType("timestamp with time zone").IsRequired();
        entity.HasIndex(value => new { value.TenantId, value.CategoryId, value.IsActive })
            .HasDatabaseName("ix_product_tenant_category_active");
    }

    private static void ConfigureProductImage(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ProductImage>();
        entity.ToTable("product_image", "catalog");
        entity.HasKey(value => new { value.ProductId, value.Position });
        entity.Property(value => value.ProductId).HasColumnName("product_id")
            .HasConversion(id => id.Value, value => new ProductId(value)).ValueGeneratedNever();
        entity.Property(value => value.Position).HasColumnName("position").ValueGeneratedNever();
        entity.Property(value => value.Url).HasColumnName("url").HasMaxLength(2048).IsRequired();
    }

    private static void ConfigureSku(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Sku>();
        entity.ToTable("sku", "catalog");
        entity.HasKey(value => value.Id);
        entity.Ignore(value => value.Size);
        entity.Ignore(value => value.ListPrice);
        entity.Property(value => value.Id).HasColumnName("id")
            .HasConversion(id => id.Value, value => new SkuId(value)).ValueGeneratedNever();
        entity.Property(value => value.ProductId).HasColumnName("product_id")
            .HasConversion(id => id.Value, value => new ProductId(value)).IsRequired();
        entity.Property(value => value.TenantId).HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value)).IsRequired();
        entity.Property(value => value.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        entity.Property(value => value.VariantName).HasColumnName("variant_name").HasMaxLength(50);
        entity.Property(value => value.WeightGram).HasColumnName("weight_gram").IsRequired();
        entity.Property(value => value.LengthCm).HasColumnName("length_cm").IsRequired();
        entity.Property(value => value.WidthCm).HasColumnName("width_cm").IsRequired();
        entity.Property(value => value.HeightCm).HasColumnName("height_cm").IsRequired();
        entity.Property(value => value.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(30);
        entity.Property(value => value.UnitCount).HasColumnName("unit_count");
        entity.Property(value => value.ListPriceAmountMinor).HasColumnName("list_price_amount_minor");
        entity.Property(value => value.ListPriceCurrency).HasColumnName("list_price_currency")
            .HasConversion<string>().HasMaxLength(3);
        entity.Property(value => value.IsActive).HasColumnName("is_active").IsRequired();
        entity.Property(value => value.CreatedAt).HasColumnName("created_at")
            .HasColumnType("timestamp with time zone").IsRequired();
        entity.HasIndex(value => new { value.TenantId, value.ProductId, value.IsActive })
            .HasDatabaseName("ix_sku_tenant_product_active");
    }
}
