using GreyGray.Platform.Messaging;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Platform;

public static partial class PlatformModelExtensions
{
    static partial void AddProcessedMessageTable(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ProcessedMessage>();

        entity.ToTable("processed_message", "platform");
        entity.HasKey(message => new { message.EventId, message.HandlerName });

        entity.Property(message => message.EventId)
            .HasColumnName("event_id")
            .ValueGeneratedNever();
        entity.Property(message => message.HandlerName)
            .HasColumnName("handler_name")
            .IsRequired();
        entity.Property(message => message.ProcessedAt)
            .HasColumnName("processed_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
    }
}
