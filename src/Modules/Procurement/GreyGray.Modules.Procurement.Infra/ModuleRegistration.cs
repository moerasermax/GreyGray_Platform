using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Modules.Procurement.Core;
using GreyGray.Platform.Abstractions.Saga;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Modules;
using GreyGray.Platform.Outbox;
using GreyGray.Platform.Saga;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GreyGray.Modules.Procurement.Infra;

/// <summary>Procurement 模組唯一對外公開的組合根。</summary>
public static class ProcurementModuleRegistration
{
    public static IServiceCollection AddProcurementModule(
        this IServiceCollection services,
        IConfiguration configuration) =>
        ProcurementModule.Register(services, configuration);
}

internal sealed class ProcurementModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_procurement";

    public static string ModuleName => "Procurement";

    public static string SchemaName => "procurement";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<ProcurementDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 Procurement 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_procurement'；請在使用 ProcurementDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });

        services.TryAddSingleton<EventTypeRegistry>();
        services.AddScoped<ProcurementApplicationService>(serviceProvider =>
        {
            var dbContext = serviceProvider.GetRequiredService<ProcurementDbContext>();
            return new ProcurementApplicationService(
                new ProcurementRepository(dbContext),
                new InquiryRepository(dbContext),
                dbContext,
                new OutboxEventPublisher<ProcurementDbContext>(
                    dbContext,
                    serviceProvider.GetRequiredService<ICorrelationContext>(),
                    serviceProvider.GetRequiredService<EventTypeRegistry>()),
                new SagaTimerScheduler<ProcurementDbContext>(
                    dbContext,
                    serviceProvider.GetRequiredService<IClock>()),
                serviceProvider.GetRequiredService<IOrderQuery>(),
                serviceProvider.GetRequiredService<ICampaignQuery>(),
                serviceProvider.GetRequiredService<IClock>(),
                serviceProvider.GetRequiredService<ICorrelationContext>());
        });
        services.AddScoped<IProcurementApplication>(serviceProvider =>
            serviceProvider.GetRequiredService<ProcurementApplicationService>());
        services.AddScoped<IProcurementGoodsReceipt>(serviceProvider =>
            serviceProvider.GetRequiredService<ProcurementApplicationService>());
        services.AddScoped<IProcurementCompensation>(serviceProvider =>
            serviceProvider.GetRequiredService<ProcurementApplicationService>());
        services.AddScoped<IInquiryReplyReceiver>(serviceProvider =>
            serviceProvider.GetRequiredService<ProcurementApplicationService>());
        services.AddScoped<IProcurementQuery>(serviceProvider =>
            serviceProvider.GetRequiredService<ProcurementApplicationService>());

        services.AddIdempotentIntegrationEventHandler<
            CampaignClosed,
            CampaignClosedHandler,
            ProcurementDbContext>();

        services.AddSagaTimeoutHandler<InquiryTimeoutHandler>();

        return services;
    }
}
