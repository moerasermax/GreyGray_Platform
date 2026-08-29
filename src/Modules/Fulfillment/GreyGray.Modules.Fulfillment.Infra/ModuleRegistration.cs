using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Fulfillment.Core;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Modules;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GreyGray.Modules.Fulfillment.Infra;

/// <summary>Fulfillment 模組唯一對外公開的組合根。</summary>
public static class FulfillmentModuleRegistration
{
    public static IServiceCollection AddFulfillmentModule(
        this IServiceCollection services,
        IConfiguration configuration) =>
        FulfillmentModule.Register(services, configuration);
}

internal sealed class FulfillmentModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_fulfillment";

    public static string ModuleName => "Fulfillment";

    public static string SchemaName => "fulfillment";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<FulfillmentDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 Fulfillment 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_fulfillment'；請在使用 FulfillmentDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });

        services.TryAddSingleton<EventTypeRegistry>();
        services.AddScoped<FulfillmentApplicationService>(serviceProvider =>
        {
            var dbContext = serviceProvider.GetRequiredService<FulfillmentDbContext>();
            return new FulfillmentApplicationService(
                new FulfillmentRepository(dbContext),
                dbContext,
                new OutboxEventPublisher<FulfillmentDbContext>(
                    dbContext,
                    serviceProvider.GetRequiredService<ICorrelationContext>(),
                    serviceProvider.GetRequiredService<EventTypeRegistry>()),
                serviceProvider.GetRequiredService<IOrderQuery>(),
                serviceProvider.GetRequiredService<IClock>(),
                serviceProvider.GetRequiredService<ICorrelationContext>());
        });
        services.AddScoped<IFulfillmentApplication>(serviceProvider =>
            serviceProvider.GetRequiredService<FulfillmentApplicationService>());
        services.AddScoped<IFulfillmentQuery>(serviceProvider =>
            serviceProvider.GetRequiredService<FulfillmentApplicationService>());

        return services;
    }
}
