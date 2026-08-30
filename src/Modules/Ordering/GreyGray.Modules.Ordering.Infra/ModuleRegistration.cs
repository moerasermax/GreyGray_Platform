using GreyGray.Modules.Checkout.Contracts;
using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Core;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Procurement.Contracts;
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

namespace GreyGray.Modules.Ordering.Infra;

/// <summary>Ordering 模組唯一對外公開的組合根。</summary>
public static class OrderingModuleRegistration
{
    public static IServiceCollection AddOrderingModule(
        this IServiceCollection services,
        IConfiguration configuration) =>
        OrderingModule.Register(services, configuration);
}

internal sealed class OrderingModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_ordering";

    public static string ModuleName => "Ordering";

    public static string SchemaName => "ordering";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<OrderingDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 Ordering 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_ordering'；請在使用 OrderingDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });

        var appraisalPeriod = TimeSpan.FromDays(
            configuration.GetValue("Ordering:AppraisalPeriodDays", 7));

        services.TryAddSingleton<EventTypeRegistry>();
        services.AddScoped<OrderingApplicationService>(serviceProvider =>
        {
            var dbContext = serviceProvider.GetRequiredService<OrderingDbContext>();
            var publisher = new OutboxEventPublisher<OrderingDbContext>(
                dbContext,
                serviceProvider.GetRequiredService<ICorrelationContext>(),
                serviceProvider.GetRequiredService<EventTypeRegistry>());
            return new OrderingApplicationService(
                new OrderingRepository(dbContext),
                dbContext,
                publisher,
                serviceProvider.GetRequiredService<Modules.Pricing.Contracts.IPricingQuotation>(),
                serviceProvider.GetRequiredService<IClock>(),
                serviceProvider.GetRequiredService<ICorrelationContext>(),
                // Storefront／Worker 目前不掛 Fulfillment 模組（Worker/Program.cs 明講
                // 「Fulfillment 等後續 M1b 模組會在各自波次納入」），這裡不能用
                // GetRequiredService——那會讓與鑑賞期無關的既有 Ordering 事件 handler
                // （CheckoutCompleted、PaymentCaptured…）在那兩個 host 直接啟動失敗。
                serviceProvider.GetService<IFulfillmentQuery>(),
                new SagaTimerScheduler<OrderingDbContext>(
                    dbContext,
                    serviceProvider.GetRequiredService<IClock>()),
                appraisalPeriod);
        });
        services.AddScoped<IOrderingApplication>(serviceProvider =>
            serviceProvider.GetRequiredService<OrderingApplicationService>());
        services.AddScoped<IOrderingGoodsReceipt>(serviceProvider =>
            serviceProvider.GetRequiredService<OrderingApplicationService>());
        services.AddScoped<IOrderingShipmentDelivery>(serviceProvider =>
            serviceProvider.GetRequiredService<OrderingApplicationService>());
        services.AddScoped<IOrderQuery>(serviceProvider =>
            serviceProvider.GetRequiredService<OrderingApplicationService>());
        services.AddScoped<ICampaignOrderQuery, CampaignOrderQueryAdapter>();

        services.AddIdempotentIntegrationEventHandler<
            CheckoutCompleted,
            CheckoutCompletedHandler,
            OrderingDbContext>();
        services.AddIdempotentIntegrationEventHandler<
            PaymentCaptured,
            PaymentCapturedHandler,
            OrderingDbContext>();
        services.AddIdempotentIntegrationEventHandler<
            PaymentFailed,
            PaymentFailedHandler,
            OrderingDbContext>();
        services.AddIdempotentIntegrationEventHandler<
            PaymentRefunded,
            PaymentRefundedHandler,
            OrderingDbContext>();
        services.AddIdempotentIntegrationEventHandler<
            ItemPurchased,
            ItemPurchasedHandler,
            OrderingDbContext>();
        services.AddIdempotentIntegrationEventHandler<
            ShipmentDelivered,
            ShipmentDeliveredHandler,
            OrderingDbContext>();

        services.AddSagaTimeoutHandler<AppraisalPeriodTimeoutHandler>();

        return services;
    }
}
