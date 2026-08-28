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
    public DbSet<CustomerCredential> CustomerCredentials => Set<CustomerCredential>();
    public DbSet<CustomerPrivateProfile> CustomerProfiles => Set<CustomerPrivateProfile>();
    public DbSet<StaffAccount> StaffAccounts => Set<StaffAccount>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("iam");
        ConfigureCustomer(modelBuilder);
        ConfigureCustomerCredential(modelBuilder);
        ConfigureCustomerProfile(modelBuilder);
        ConfigureStaff(modelBuilder);
        ConfigureAddress(modelBuilder);
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

    private static void ConfigureCustomerCredential(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CustomerCredential>();
        entity.ToTable("customer_credential", "iam");
        entity.HasKey(value => value.CustomerId);
        entity.Property(value => value.CustomerId).HasColumnName("customer_id")
            .HasConversion(id => id.Value, value => new CustomerId(value)).ValueGeneratedNever();
        entity.Property(value => value.TenantId).HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value)).IsRequired();
        entity.Property(value => value.PhoneLookup).HasColumnName("phone_lookup").HasMaxLength(64).IsRequired();
        entity.Property(value => value.PhoneNumberMasked).HasColumnName("phone_masked").HasMaxLength(10).IsRequired();
        entity.Property(value => value.PasswordHash).HasColumnName("password_hash").HasMaxLength(256).IsRequired();
        entity.Property(value => value.CreatedAt).HasColumnName("created_at")
            .HasColumnType("timestamp with time zone").IsRequired();
        entity.HasIndex(value => new { value.TenantId, value.PhoneLookup })
            .IsUnique().HasDatabaseName("ux_customer_credential_tenant_phone");
    }

    private static void ConfigureCustomerProfile(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CustomerPrivateProfile>();
        entity.ToTable("customer_profile", "iam");
        entity.HasKey(value => value.CustomerId);
        entity.Property(value => value.CustomerId).HasColumnName("customer_id")
            .HasConversion(id => id.Value, value => new CustomerId(value)).ValueGeneratedNever();
        entity.Property(value => value.EncryptedEmail).HasColumnName("email_ciphertext");
        entity.Property(value => value.LineLinked).HasColumnName("line_linked").IsRequired();
    }

    private static void ConfigureStaff(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<StaffAccount>();
        entity.ToTable("staff_account", "iam");
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).HasColumnName("id")
            .HasConversion(id => id.Value, value => new StaffId(value)).ValueGeneratedNever();
        entity.Property(value => value.TenantId).HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value)).IsRequired();
        entity.Property(value => value.DisplayName).HasColumnName("display_name").HasMaxLength(50).IsRequired();
        entity.Property(value => value.EmailLookup).HasColumnName("email_lookup").HasMaxLength(64).IsRequired();
        entity.Property(value => value.EncryptedEmail).HasColumnName("email_ciphertext").IsRequired();
        entity.Property(value => value.PasswordHash).HasColumnName("password_hash").HasMaxLength(256).IsRequired();
        entity.Property(value => value.Role).HasColumnName("role").HasConversion<short>().IsRequired();
        entity.Property(value => value.IsActive).HasColumnName("is_active").IsRequired();
        entity.Property(value => value.CreatedAt).HasColumnName("created_at")
            .HasColumnType("timestamp with time zone").IsRequired();
        entity.HasIndex(value => new { value.TenantId, value.EmailLookup })
            .IsUnique().HasDatabaseName("ux_staff_account_tenant_email");
    }

    private static void ConfigureAddress(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CustomerAddress>();
        entity.ToTable("customer_address", "iam");
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).HasColumnName("id")
            .HasConversion(id => id.Value, value => new AddressId(value)).ValueGeneratedNever();
        entity.Property(value => value.CustomerId).HasColumnName("customer_id")
            .HasConversion(id => id.Value, value => new CustomerId(value)).IsRequired();
        entity.Property(value => value.TenantId).HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value)).IsRequired();
        entity.Property(value => value.RecipientName).HasColumnName("recipient_name_ciphertext").IsRequired();
        entity.Property(value => value.PhoneNumber).HasColumnName("phone_ciphertext").IsRequired();
        entity.Property(value => value.PostalCode).HasColumnName("postal_code_ciphertext").IsRequired();
        entity.Property(value => value.City).HasColumnName("city_ciphertext").IsRequired();
        entity.Property(value => value.District).HasColumnName("district_ciphertext").IsRequired();
        entity.Property(value => value.StreetAddress).HasColumnName("street_address_ciphertext").IsRequired();
        entity.Property(value => value.IsDefault).HasColumnName("is_default").IsRequired();
        entity.Property(value => value.CreatedAt).HasColumnName("created_at")
            .HasColumnType("timestamp with time zone").IsRequired();
        entity.HasIndex(value => new { value.CustomerId, value.IsDefault })
            .HasFilter("is_default = true")
            .IsUnique()
            .HasDatabaseName("ux_customer_address_default");
    }
}
