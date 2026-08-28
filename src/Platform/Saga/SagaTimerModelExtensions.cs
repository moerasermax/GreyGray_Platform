using GreyGray.Platform.Saga;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Platform;

public static partial class PlatformModelExtensions
{
    static partial void AddSagaTimerTable(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SagaTimer>();
        entity.ToTable("saga_timer", "platform");
        entity.HasKey(timer => timer.Id);

        entity.Property(timer => timer.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        entity.Property(timer => timer.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(
                tenantId => tenantId.Value,
                value => new TenantId(value));
        entity.Property(timer => timer.SagaType)
            .HasColumnName("saga_type");
        entity.Property(timer => timer.SagaId)
            .HasColumnName("saga_id");
        entity.Property(timer => timer.FireAt)
            .HasColumnName("fire_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(timer => timer.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb");
        entity.Property(timer => timer.FiredAt)
            .HasColumnName("fired_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(timer => timer.CancelledAt)
            .HasColumnName("cancelled_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(timer => timer.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");

        entity.HasIndex(timer => timer.FireAt)
            .HasDatabaseName("ix_saga_timer_pending")
            .HasFilter("fired_at IS NULL AND cancelled_at IS NULL");
        entity.HasIndex(timer => new { timer.SagaType, timer.SagaId })
            .HasDatabaseName("ix_saga_timer_saga")
            .HasFilter("fired_at IS NULL AND cancelled_at IS NULL");
    }
}
