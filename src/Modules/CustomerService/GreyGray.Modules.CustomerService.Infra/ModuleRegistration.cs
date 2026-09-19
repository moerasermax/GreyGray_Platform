using GreyGray.Modules.CustomerService.Contracts;
using GreyGray.Modules.CustomerService.Core;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Modules;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GreyGray.Modules.CustomerService.Infra;

/// <summary>CustomerService 模組唯一對外公開的組合根。</summary>
public static class CustomerServiceModuleRegistration
{
    /// <summary>註冊 CustomerService 模組。</summary>
    public static IServiceCollection AddCustomerServiceModule(
        this IServiceCollection services,
        IConfiguration configuration) =>
        CustomerServiceModule.Register(services, configuration);
}

internal sealed class CustomerServiceModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_customer_service";

    public static string ModuleName => "CustomerService";

    public static string SchemaName => "customer_service";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<CustomerServiceDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 CustomerService 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_customer_service'；請在使用 " +
                    "CustomerServiceDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });

        services.AddScoped<IUnitOfWork>(serviceProvider =>
            serviceProvider.GetRequiredService<CustomerServiceDbContext>());
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<ICustomerServiceTickets, CustomerServiceTicketService>();

        return services;
    }
}
