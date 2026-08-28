using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Campaign.Core;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Modules;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GreyGray.Modules.Campaign.Infra;

/// <summary>Campaign 模組唯一對外公開的組合根。</summary>
public static class CampaignModuleRegistration
{
    /// <summary>註冊 Campaign 模組。</summary>
    public static IServiceCollection AddCampaignModule(
        this IServiceCollection services,
        IConfiguration configuration) =>
        CampaignModule.Register(services, configuration);
}

internal sealed class CampaignModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_campaign";

    public static string ModuleName => "Campaign";

    public static string SchemaName => "campaign";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<CampaignDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 Campaign 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_campaign'；請在使用 CampaignDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });

        services.TryAddSingleton<EventTypeRegistry>();
        services.AddScoped<CampaignService>(serviceProvider =>
        {
            var dbContext = serviceProvider.GetRequiredService<CampaignDbContext>();
            return new CampaignService(
                new CampaignRepository(dbContext),
                dbContext,
                new OutboxEventPublisher<CampaignDbContext>(
                    dbContext,
                    serviceProvider.GetRequiredService<ICorrelationContext>(),
                    serviceProvider.GetRequiredService<EventTypeRegistry>()),
                serviceProvider.GetRequiredService<ICatalogQuery>(),
                serviceProvider.GetRequiredService<ICampaignOrderQuery>(),
                serviceProvider.GetRequiredService<IClock>(),
                serviceProvider.GetRequiredService<ICorrelationContext>());
        });
        services.AddScoped<ICampaignQuery>(serviceProvider =>
            serviceProvider.GetRequiredService<CampaignService>());
        services.AddScoped<ICampaignStorefront>(serviceProvider =>
            serviceProvider.GetRequiredService<CampaignService>());
        services.AddScoped<ICampaignAdministration>(serviceProvider =>
            serviceProvider.GetRequiredService<CampaignService>());

        return services;
    }
}
