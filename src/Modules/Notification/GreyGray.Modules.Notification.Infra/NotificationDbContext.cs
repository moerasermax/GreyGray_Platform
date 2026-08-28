using GreyGray.Modules.Notification.Contracts;
using GreyGray.Platform;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Notification.Infra;

internal sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options)
{
    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("notify");
        ConfigureNotification(modelBuilder);
        modelBuilder.AddPlatformTables();
    }

    private static void ConfigureNotification(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<NotificationEntity>();
        entity.ToTable("notification", "notify");
        entity.HasKey(notification => notification.Id);
        entity.Property(notification => notification.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new NotificationId(value))
            .ValueGeneratedNever();
        entity.Property(notification => notification.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();
        entity.Property(notification => notification.CustomerId)
            .HasColumnName("customer_id")
            .IsRequired();
        entity.Property(notification => notification.Channel)
            .HasColumnName("channel")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(notification => notification.TemplateCode)
            .HasColumnName("template_code")
            .HasMaxLength(100)
            .IsRequired();
        entity.Property(notification => notification.Status)
            .HasColumnName("status")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(notification => notification.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.Property(notification => notification.TraceId)
            .HasColumnName("trace_id")
            .HasMaxLength(32)
            .IsRequired();
        entity.Property(notification => notification.HandlerSpanId)
            .HasColumnName("handler_span_id")
            .HasMaxLength(16);
        entity.HasIndex(notification => new
            {
                notification.TenantId,
                notification.CustomerId,
                notification.CreatedAt,
            })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_notification_tenant_customer_created");
    }
}
