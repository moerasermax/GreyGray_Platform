using GreyGray.Platform;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Identity.Infra;

/// <summary>Identity 模組專用的資料庫工作單元。</summary>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("iam");
        modelBuilder.AddPlatformTables();
    }
}
