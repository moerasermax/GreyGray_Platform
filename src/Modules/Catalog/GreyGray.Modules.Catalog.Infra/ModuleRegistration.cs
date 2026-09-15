using GreyGray.Platform.Modules;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Catalog.Core;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GreyGray.Modules.Catalog.Infra;

/// <summary>Catalog 模組唯一對外公開的組合根。</summary>
public static class CatalogModuleRegistration
{
    /// <summary>註冊 Catalog 模組的基礎設施服務。</summary>
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services,
        IConfiguration configuration) =>
        CatalogModule.Register(services, configuration);
}

/// <summary>提供 Platform 統一辨識與註冊 Catalog 模組的內部接縫。</summary>
internal sealed class CatalogModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_catalog";

    public static string ModuleName => "Catalog";

    public static string SchemaName => "catalog";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // options factory 在真正解析 CatalogDbContext 時才執行，讓尚未掛載模組資料庫的
        // Host 仍可啟動並提供 /health；一旦使用模組則立即以明確訊息失敗。
        services.AddDbContext<CatalogDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 Catalog 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_catalog'；請在使用 CatalogDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });

        services.TryAddSingleton<EventTypeRegistry>();
        services.AddScoped<CatalogRepository>();
        services.AddScoped<ICatalogRepository>(serviceProvider =>
            serviceProvider.GetRequiredService<CatalogRepository>());
        services.AddScoped<CatalogService>(serviceProvider =>
        {
            var dbContext = serviceProvider.GetRequiredService<CatalogDbContext>();
            var publisher = new OutboxEventPublisher<CatalogDbContext>(
                dbContext,
                serviceProvider.GetRequiredService<ICorrelationContext>(),
                serviceProvider.GetRequiredService<EventTypeRegistry>());
            return new CatalogService(
                serviceProvider.GetRequiredService<ICatalogRepository>(),
                publisher,
                serviceProvider.GetRequiredService<IClock>(),
                serviceProvider.GetRequiredService<ICorrelationContext>());
        });
        services.AddScoped<ICatalogQuery>(serviceProvider =>
            serviceProvider.GetRequiredService<CatalogService>());
        services.AddScoped<IStorefrontCatalogQuery>(serviceProvider =>
            serviceProvider.GetRequiredService<CatalogService>());
        services.AddScoped<IStorefrontFavorites>(serviceProvider =>
            serviceProvider.GetRequiredService<CatalogService>());
        services.AddScoped<ICatalogAdministration>(serviceProvider =>
            serviceProvider.GetRequiredService<CatalogService>());

        return services;
    }
}
