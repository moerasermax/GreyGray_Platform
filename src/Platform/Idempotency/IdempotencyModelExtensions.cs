using GreyGray.Platform.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Platform;

public static partial class PlatformModelExtensions
{
    static partial void AddIdempotencyTable(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<IdempotencyKey>();
        entity.ToTable("idempotency_key", "platform");
        entity.HasKey(entry => new { entry.Key, entry.Scope });

        entity.Property(entry => entry.Key)
            .HasColumnName("key")
            .HasMaxLength(255);
        entity.Property(entry => entry.Scope)
            .HasColumnName("scope");
        entity.Property(entry => entry.RequestHash)
            .HasColumnName("request_hash");
        entity.Property(entry => entry.Status)
            .HasColumnName("status");
        entity.Property(entry => entry.ResponseSnapshot)
            .HasColumnName("response_snapshot")
            .HasColumnType("jsonb");
        entity.Property(entry => entry.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(entry => entry.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone");

        entity.HasIndex(entry => entry.ExpiresAt)
            .HasDatabaseName("ix_idempotency_expiry");
    }
}
