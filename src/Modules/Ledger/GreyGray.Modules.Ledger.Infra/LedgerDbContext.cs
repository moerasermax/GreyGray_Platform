using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Ledger.Contracts;
using GreyGray.Modules.Ledger.Core;
using GreyGray.Platform;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Ledger.Infra;

internal sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<LedgerAccount> Accounts => Set<LedgerAccount>();

    public DbSet<JournalEntry> Entries => Set<JournalEntry>();

    public DbSet<JournalLine> Lines => Set<JournalLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("ledger");

        var account = modelBuilder.Entity<LedgerAccount>();
        account.ToTable("account", "ledger");
        account.HasKey(value => value.Id);
        account.HasAlternateKey(value => new { value.TenantId, value.Id });
        account.Property(value => value.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new AccountId(value))
            .ValueGeneratedNever();
        account.Property(value => value.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();
        account.Property(value => value.Code)
            .HasColumnName("code")
            .HasMaxLength(16)
            .IsRequired();
        account.Property(value => value.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();
        account.Property(value => value.Type)
            .HasColumnName("type")
            .HasConversion<short>()
            .IsRequired();
        account.HasIndex(value => new { value.TenantId, value.Code })
            .IsUnique()
            .HasDatabaseName("ux_account_tenant_code");

        var entry = modelBuilder.Entity<JournalEntry>();
        entry.ToTable("journal_entry", "ledger");
        entry.HasKey(value => value.Id);
        entry.HasAlternateKey(value => new { value.TenantId, value.Id });
        entry.Property(value => value.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new EntryId(value))
            .ValueGeneratedNever();
        entry.Property(value => value.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();
        entry.Property(value => value.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entry.Property(value => value.PostedAt)
            .HasColumnName("posted_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entry.Property(value => value.SourceModule)
            .HasColumnName("source_module")
            .HasMaxLength(64)
            .IsRequired();
        entry.Property(value => value.SourceRef)
            .HasColumnName("source_ref")
            .HasMaxLength(128)
            .IsRequired();
        entry.Property(value => value.Memo)
            .HasColumnName("memo")
            .HasMaxLength(500)
            .IsRequired();
        entry.HasIndex(value => new { value.TenantId, value.SourceModule, value.SourceRef })
            .IsUnique()
            .HasDatabaseName("ux_journal_source");
        entry.HasIndex(value => new { value.TenantId, value.PostedAt, value.Id })
            .HasDatabaseName("ix_journal_tenant_posted");

        var line = modelBuilder.Entity<JournalLine>();
        line.ToTable("journal_line", "ledger");
        line.HasKey(value => value.Id);
        line.Property(value => value.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        line.Property(value => value.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();
        line.Property(value => value.EntryId)
            .HasColumnName("entry_id")
            .HasConversion(id => id.Value, value => new EntryId(value))
            .IsRequired();
        line.Property(value => value.AccountId)
            .HasColumnName("account_id")
            .HasConversion(id => id.Value, value => new AccountId(value))
            .IsRequired();
        line.Property(value => value.AccountCode)
            .HasColumnName("account_code")
            .HasMaxLength(16)
            .IsRequired();
        line.Property(value => value.Direction)
            .HasColumnName("direction")
            .HasConversion<short>()
            .IsRequired();
        line.Property(value => value.AmountMinor)
            .HasColumnName("amount_minor")
            .IsRequired();
        line.Property(value => value.Currency)
            .HasColumnName("currency")
            .HasConversion<string>()
            .HasMaxLength(3)
            .IsRequired();
        line.Property(value => value.CampaignId)
            .HasColumnName("campaign_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CampaignId(value.Value) : null);
        line.Property(value => value.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CustomerId(value.Value) : null);
        entry.HasMany(value => value.Lines)
            .WithOne()
            .HasForeignKey(value => new { value.TenantId, value.EntryId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entry.Navigation(value => value.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        line.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.AccountId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        line.HasIndex(value => new { value.CampaignId, value.AccountCode })
            .HasDatabaseName("ix_journal_line_campaign_account");
        line.HasIndex(value => new { value.CustomerId, value.AccountCode })
            .HasDatabaseName("ix_journal_line_customer_account");

        modelBuilder.AddPlatformTables();
    }
}
