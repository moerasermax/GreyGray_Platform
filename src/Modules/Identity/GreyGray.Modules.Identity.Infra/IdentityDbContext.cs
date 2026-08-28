using GreyGray.Platform;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Identity.Core;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Identity.Infra;

/// <summary>Identity 模組專用的資料庫工作單元。</summary>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("iam");
        ConfigureCustomer(modelBuilder);
        modelBuilder.AddPlatformTables();
    }

    private static void ConfigureCustomer(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Customer>();
        entity.ToTable("customer", "iam");
        entity.HasKey(customer => customer.Id);
        entity.Property(customer => customer.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CustomerId(value))
            .ValueGeneratedNever();
        entity.Property(customer => customer.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();
        entity.Property(customer => customer.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(50)
            .IsRequired();
        entity.Property(customer => customer.Tier)
            .HasColumnName("tier")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(customer => customer.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        entity.Property(customer => customer.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.HasIndex(customer => new { customer.TenantId, customer.CreatedAt })
            .HasDatabaseName("ix_customer_tenant_created");
    }
}
