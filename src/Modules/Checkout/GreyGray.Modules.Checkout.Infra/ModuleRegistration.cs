using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Checkout.Core;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Modules;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GreyGray.Modules.Checkout.Infra;

/// <summary>Checkout 模組唯一對外公開的組合根。</summary>
public static class CheckoutModuleRegistration
{
    public static IServiceCollection AddCheckoutModule(
        this IServiceCollection services,
        IConfiguration configuration) =>
        CheckoutModule.Register(services, configuration);
}

internal sealed class CheckoutModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_checkout";

    public static string ModuleName => "Checkout";

    public static string SchemaName => "checkout";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<CheckoutDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 Checkout 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_checkout'；請在使用 CheckoutDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });

        services.TryAddSingleton<EventTypeRegistry>();
        services.AddScoped<CheckoutApplicationService>(serviceProvider =>
        {
            var dbContext = serviceProvider.GetRequiredService<CheckoutDbContext>();
            var publisher = new OutboxEventPublisher<CheckoutDbContext>(
                dbContext,
                serviceProvider.GetRequiredService<ICorrelationContext>(),
                serviceProvider.GetRequiredService<EventTypeRegistry>());
            return new CheckoutApplicationService(
                new CheckoutRepository(dbContext),
                dbContext,
                publisher,
                serviceProvider.GetRequiredService<Modules.Catalog.Contracts.ICatalogQuery>(),
                serviceProvider.GetRequiredService<Modules.Campaign.Contracts.ICampaignQuery>(),
                serviceProvider.GetRequiredService<Modules.Inventory.Contracts.IInventoryQuery>(),
                serviceProvider.GetRequiredService<Modules.Pricing.Contracts.IPricingQuotation>(),
                serviceProvider.GetRequiredService<Modules.Payment.Contracts.IPaymentQuery>(),
                serviceProvider.GetRequiredService<Modules.Identity.Contracts.ICustomerDirectory>(),
                serviceProvider.GetRequiredService<IClock>(),
                serviceProvider.GetRequiredService<ICorrelationContext>());
        });
        services.AddScoped<ICheckoutApplication>(serviceProvider =>
            serviceProvider.GetRequiredService<CheckoutApplicationService>());
        services.AddScoped<ICheckoutQuery>(serviceProvider =>
            serviceProvider.GetRequiredService<CheckoutApplicationService>());

        return services;
    }
}
