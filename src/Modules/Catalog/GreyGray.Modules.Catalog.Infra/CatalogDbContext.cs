using GreyGray.Platform;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.Catalog.Infra;

/// <summary>Catalog 模組專用的資料庫工作單元。</summary>
internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("catalog");
        modelBuilder.AddPlatformTables();
    }
}
