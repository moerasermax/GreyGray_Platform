using GreyGray.Platform.Outbox;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Platform;

/// <summary>
/// Platform 背景工作使用的 DbContext。
/// 業務模組發布事件時不使用這個 Context，而是在自己的 DbContext 套用
/// <see cref="PlatformModelExtensions.AddPlatformTables"/>，讓業務資料與 outbox 共用同一個交易。
/// </summary>
public sealed partial class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddPlatformTables();
    }
}
