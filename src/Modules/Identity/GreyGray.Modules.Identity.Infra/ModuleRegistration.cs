using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Identity.Core;
using GreyGray.Platform.Abstractions.Audit;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Modules;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GreyGray.Modules.Identity.Infra;

/// <summary>Identity 模組唯一對外公開的組合根。</summary>
public static class IdentityModuleRegistration
{
    /// <summary>註冊 Identity 模組的基礎設施服務。</summary>
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration) =>
        IdentityModule.Register(services, configuration);
}

/// <summary>Identity 模組的內部登錄描述。</summary>
internal sealed class IdentityModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_iam";

    public static string ModuleName => "Identity";

    public static string SchemaName => "iam";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // options factory 在真正解析 IdentityDbContext 時才執行，讓尚未掛載模組資料庫的
        // Host 仍可啟動並提供 /health；一旦使用模組則立即以明確訊息失敗。
        services.AddDbContext<IdentityDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 Identity 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_iam'；請在使用 IdentityDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });

        services.TryAddSingleton<EventTypeRegistry>();
        services.AddSingleton(serviceProvider =>
        {
            var configuredKey = configuration["Identity:DataProtectionKey"];
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                throw new InvalidOperationException(
                    "缺少 Identity 個資保護金鑰 'Identity:DataProtectionKey'；" +
                    "請設定 Base64 編碼的 32-byte AES-256 金鑰後再使用 Identity 帳號服務。");
            }

            return new IdentityDataProtector(configuredKey);
        });
        services.AddSingleton<IIdentityDataProtector>(serviceProvider =>
            serviceProvider.GetRequiredService<IdentityDataProtector>());
        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<IStaffRolePolicy, StaffRolePolicy>();
        services.AddScoped<IdentityCustomerRepository>();
        services.AddScoped<IIdentityRepository>(serviceProvider =>
            serviceProvider.GetRequiredService<IdentityCustomerRepository>());
        services.AddScoped<ICustomerRepository>(serviceProvider =>
            serviceProvider.GetRequiredService<IdentityCustomerRepository>());
        services.AddScoped<CustomerAccountService>(serviceProvider =>
        {
            var dbContext = serviceProvider.GetRequiredService<IdentityDbContext>();
            return new CustomerAccountService(
                serviceProvider.GetRequiredService<IIdentityRepository>(),
                serviceProvider.GetRequiredService<IdentityDataProtector>(),
                serviceProvider.GetRequiredService<PasswordHasher>(),
                CreatePublisher(dbContext, serviceProvider),
                serviceProvider.GetRequiredService<IClock>(),
                serviceProvider.GetRequiredService<ICorrelationContext>());
        });
        services.AddScoped<ICustomerAccounts>(serviceProvider =>
            serviceProvider.GetRequiredService<CustomerAccountService>());
        services.AddScoped<ICustomerAddressBook, CustomerAddressService>();
        services.AddScoped<ICustomerDirectory>(serviceProvider =>
            new CustomerDirectoryService(
                serviceProvider.GetRequiredService<IIdentityRepository>(),
                serviceProvider.GetRequiredService<IIdentityDataProtector>(),
                serviceProvider.GetService<IAuditWriter>(),
                serviceProvider.GetRequiredService<ICorrelationContext>()));
        services.AddScoped<StaffAccountService>();
        services.AddScoped<IStaffAccounts>(serviceProvider =>
            serviceProvider.GetRequiredService<StaffAccountService>());
        services.AddScoped<IStaffDirectory>(serviceProvider =>
            serviceProvider.GetRequiredService<StaffAccountService>());
        services.AddScoped<ICustomerProvisioning>(serviceProvider =>
        {
            var dbContext = serviceProvider.GetRequiredService<IdentityDbContext>();
            return new CustomerProvisioningService(
                serviceProvider.GetRequiredService<ICustomerRepository>(),
                dbContext,
                CreatePublisher(dbContext, serviceProvider),
                serviceProvider.GetRequiredService<IClock>(),
                serviceProvider.GetRequiredService<ICorrelationContext>());
        });

        return services;
    }

    private static OutboxEventPublisher<IdentityDbContext> CreatePublisher(
        IdentityDbContext dbContext,
        IServiceProvider serviceProvider) =>
        new(
            dbContext,
            serviceProvider.GetRequiredService<ICorrelationContext>(),
            serviceProvider.GetRequiredService<EventTypeRegistry>());
}
