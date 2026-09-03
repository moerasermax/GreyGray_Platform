using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ledger.Contracts;
using GreyGray.Modules.Ledger.Core;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Procurement.Contracts;
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
        services.AddIdempotentIntegrationEventHandler<
            GoodsReceived,
            GoodsReceivedLedgerHandler,
            LedgerDbContext>();
        services.AddIdempotentIntegrationEventHandler<
            TripCostRecorded,
            TripCostRecordedLedgerHandler,
            LedgerDbContext>();
        // ★ 只有本地批發進貨會在這裡入帳；handler 自己會把其他來源擋掉。
        // 代購那條線的進貨成本由上面的 GoodsReceivedLedgerHandler 記，
        // 而 Inventory 建完批號之後還會再發一次 LotCreated——兩邊都記就是雙重入帳。
        services.AddIdempotentIntegrationEventHandler<
            LotCreated,
            LotCreatedLedgerHandler,
            LedgerDbContext>();
        // docs/02 §5 第 ⑦ 階段的兩筆。型別與文件從第一天就有，但一直沒有人訂閱，
        // 所以出貨那一段在正式機上一毛都沒落帳（#43）；
        // LedgerCoverageTests 現在會機械擋住同一種漏接。
        services.AddIdempotentIntegrationEventHandler<
            StockCostAllocated,
            StockCostAllocatedLedgerHandler,
            LedgerDbContext>();
        services.AddIdempotentIntegrationEventHandler<
            ShipmentDispatched,
            ShipmentDispatchedLedgerHandler,
            LedgerDbContext>();
        return services;
    }
}
