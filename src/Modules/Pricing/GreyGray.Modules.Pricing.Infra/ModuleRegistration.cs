using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Modules.Pricing.Core;
using GreyGray.Platform.Modules;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GreyGray.Modules.Pricing.Infra;

/// <summary>Pricing 模組唯一對外公開的組合根。</summary>
public static class PricingModuleRegistration
{
    /// <summary>註冊 Pricing 模組。</summary>
    public static IServiceCollection AddPricingModule(
        this IServiceCollection services,
        IConfiguration configuration) =>
        PricingModule.Register(services, configuration);
}

internal sealed class PricingModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_pricing";

    public static string ModuleName => "Pricing";

    public static string SchemaName => "pricing";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<PricingDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 Pricing 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_pricing'；請在使用 PricingDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });

        services.AddScoped<IPricingQuotation>(serviceProvider =>
        {
            var dbContext = serviceProvider.GetRequiredService<PricingDbContext>();
            return new FlatRatePricingService(
                new PricingSnapshotStore(
                    dbContext,
                    serviceProvider.GetRequiredService<ICorrelationContext>()),
                dbContext,
                serviceProvider.GetRequiredService<IClock>());
        });

        return services;
    }
}
