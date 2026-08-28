using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GreyGray.Platform;

/// <summary>把 Platform 共用表加入呼叫端的 EF model。</summary>
public static partial class PlatformModelExtensions
{
    /// <summary>
    /// 加入目前已實作的 Platform 共用表。
    /// 每個業務模組的 DbContext 都必須呼叫此方法，才能讓業務資料與 outbox 由同一次
    /// <c>SaveChanges</c> 寫入同一個資料庫交易。
    /// </summary>
    public static ModelBuilder AddPlatformTables(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureOutboxMessage(modelBuilder.Entity<OutboxMessage>());
        AddProcessedMessageTable(modelBuilder);
        AddIdempotencyTable(modelBuilder);
        AddSagaTimerTable(modelBuilder);
        return modelBuilder;
    }

    private static void ConfigureOutboxMessage(EntityTypeBuilder<OutboxMessage> entity)
    {
        entity.ToTable("outbox_message", "platform");
        entity.HasKey(message => message.Id);

        entity.Property(message => message.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        entity.Property(message => message.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(
                tenantId => tenantId.Value,
                value => new TenantId(value))
            .IsRequired();

        entity.Property(message => message.AggregateType)
            .HasColumnName("aggregate_type")
            .IsRequired();
        entity.Property(message => message.AggregateId)
            .HasColumnName("aggregate_id")
            .IsRequired();
        entity.Property(message => message.EventType)
            .HasColumnName("event_type")
            .IsRequired();
        entity.Property(message => message.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(message => message.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.Property(message => message.CorrelationId)
            .HasColumnName("correlation_id")
            .IsRequired();
        entity.Property(message => message.CausationId)
            .HasColumnName("causation_id");

        entity.Property(message => message.ProcessedAt)
            .HasColumnName("processed_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(message => message.Attempts)
            .HasColumnName("attempts")
            .HasDefaultValue(0)
            .IsRequired();
        entity.Property(message => message.NextAttemptAt)
            .HasColumnName("next_attempt_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
        entity.Property(message => message.LastError)
            .HasColumnName("last_error");
        entity.Property(message => message.IsDeadLettered)
            .HasColumnName("dead_lettered")
            .HasDefaultValue(false)
            .IsRequired();

        entity.HasIndex(message => message.NextAttemptAt)
            .HasDatabaseName("ix_outbox_pending")
            .HasFilter("processed_at IS NULL AND dead_lettered = false");
        entity.HasIndex(message => new
            {
                message.AggregateType,
                message.AggregateId,
                message.OccurredAt,
            })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_outbox_aggregate");
        entity.HasIndex(message => message.OccurredAt)
            .IsDescending()
            .HasDatabaseName("ix_outbox_dead_letter")
            .HasFilter("dead_lettered = true");
    }

    // 後續工作包在自己的所有權檔案實作這些 partial hooks，避免多人同時修改本檔。
    static partial void AddProcessedMessageTable(ModelBuilder modelBuilder);

    static partial void AddIdempotencyTable(ModelBuilder modelBuilder);

    static partial void AddSagaTimerTable(ModelBuilder modelBuilder);
}
