using GreyGray.Modules.Ledger.Contracts;
using GreyGray.Modules.Ledger.Core;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Modules;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GreyGray.Modules.Ledger.Infra;

public static class LedgerModuleRegistration
{
    public static IServiceCollection AddLedgerModule(
        this IServiceCollection services,
        IConfiguration configuration) => LedgerModule.Register(services, configuration);
}

internal sealed class LedgerModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_ledger";

    public static string ModuleName => "Ledger";

    public static string SchemaName => "ledger";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<LedgerDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 Ledger 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_ledger'；請在使用 LedgerDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });

        services.TryAddSingleton<EventTypeRegistry>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<LedgerPostingService>(provider =>
        {
            var dbContext = provider.GetRequiredService<LedgerDbContext>();
            return new LedgerPostingService(
                provider.GetRequiredService<ILedgerRepository>(),
                new OutboxEventPublisher<LedgerDbContext>(
                    dbContext,
                    provider.GetRequiredService<ICorrelationContext>(),
                    provider.GetRequiredService<EventTypeRegistry>()),
                provider.GetRequiredService<IClock>());
        });
        services.AddScoped<LedgerQuery>();
        services.AddScoped<ILedgerQuery>(provider => provider.GetRequiredService<LedgerQuery>());
        services.AddScoped<IStoredValueQuery>(provider => provider.GetRequiredService<LedgerQuery>());

        services.AddIdempotentIntegrationEventHandler<
            PaymentCaptured,
            PaymentCapturedLedgerHandler,
            LedgerDbContext>();
        services.AddIdempotentIntegrationEventHandler<
            PaymentRefunded,
            PaymentRefundedLedgerHandler,
            LedgerDbContext>();
        services.AddIdempotentIntegrationEventHandler<
            PayoutSettled,
            PayoutSettledLedgerHandler,
            LedgerDbContext>();
        services.AddIdempotentIntegrationEventHandler<
            OrderCompleted,
            OrderCompletedLedgerHandler,
            LedgerDbContext>();
        return services;
    }
}
