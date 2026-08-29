using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GreyGray.Modules.Notification.Infra;

/// <summary>Notification 模組唯一對外公開的組合根。</summary>
public static class NotificationModuleRegistration
{
    public static IServiceCollection AddNotificationModule(
        this IServiceCollection services,
        IConfiguration configuration) =>
        NotificationModule.Register(services, configuration);
}

internal sealed class NotificationModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_notify";

    public static string ModuleName => "Notification";

    public static string SchemaName => "notify";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<NotificationDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 Notification 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_notify'；請在使用 NotificationDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });

        services.AddIdempotentIntegrationEventHandler<
            CustomerRegistered,
            CustomerRegisteredNotificationHandler,
            NotificationDbContext>();

        services.AddIdempotentIntegrationEventHandler<
            ItemPriceChanged,
            ItemPriceChangedNotificationHandler,
            NotificationDbContext>();

        return services;
    }
}
