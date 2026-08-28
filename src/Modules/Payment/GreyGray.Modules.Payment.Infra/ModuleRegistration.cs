using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Payment.Core;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Modules;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GreyGray.Modules.Payment.Infra;

public static class PaymentModuleRegistration
{
    public static IServiceCollection AddPaymentModule(
        this IServiceCollection services,
        IConfiguration configuration) => PaymentModule.Register(services, configuration);
}

internal sealed class PaymentModule : IModuleRegistration
{
    private const string ConnectionStringName = "GreyGray_payment";

    public static string ModuleName => "Payment";

    public static string SchemaName => "payment";

    public static IServiceCollection Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<PaymentDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "缺少 Payment 模組資料庫連線字串 " +
                    "'ConnectionStrings:GreyGray_payment'；請在使用 PaymentDbContext 前完成設定。");
            }

            options.UseNpgsql(connectionString);
        });

        services.TryAddSingleton<EventTypeRegistry>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IEcpayGateway>(provider =>
        {
            var (settings, hashKey, hashIv) = ReadEcpaySettings(configuration);
            return new EcpayGateway(settings, hashKey, hashIv);
        });
        services.AddScoped(provider => ReadEcpaySettings(configuration).Settings);
        services.AddScoped<PaymentApplicationService>(provider =>
        {
            var dbContext = provider.GetRequiredService<PaymentDbContext>();
            return new PaymentApplicationService(
                provider.GetRequiredService<IPaymentRepository>(),
                dbContext,
                new OutboxEventPublisher<PaymentDbContext>(
                    dbContext,
                    provider.GetRequiredService<ICorrelationContext>(),
                    provider.GetRequiredService<EventTypeRegistry>()),
                provider.GetRequiredService<IEcpayGateway>(),
                provider.GetRequiredService<EcpaySettings>(),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<ICorrelationContext>());
        });
        services.AddScoped<IPaymentCommand>(provider =>
            provider.GetRequiredService<PaymentApplicationService>());
        services.AddScoped<IPaymentQuery>(provider =>
            provider.GetRequiredService<PaymentApplicationService>());
        services.AddScoped<RefundRequestedHandler>(provider =>
        {
            var dbContext = provider.GetRequiredService<PaymentDbContext>();
            return new RefundRequestedHandler(
                provider.GetRequiredService<IPaymentRepository>(),
                new OutboxEventPublisher<PaymentDbContext>(
                    dbContext,
                    provider.GetRequiredService<ICorrelationContext>(),
                    provider.GetRequiredService<EventTypeRegistry>()),
                provider.GetRequiredService<IClock>());
        });
        services.AddIdempotentIntegrationEventHandler<
            PaymentRequested,
            PaymentRequestedHandler,
            PaymentDbContext>();
        services.AddIdempotentIntegrationEventHandler<
            RefundRequested,
            RefundRequestedHandler,
            PaymentDbContext>();
        return services;
    }

    private static (EcpaySettings Settings, string HashKey, string HashIv) ReadEcpaySettings(
        IConfiguration configuration)
    {
        var merchantId = Required(configuration, "Payment:ECPay:MerchantId");
        var hashKey = Required(configuration, "Payment:ECPay:HashKey");
        var hashIv = Required(configuration, "Payment:ECPay:HashIV");
        var checkout = configuration["Payment:ECPay:CheckoutUrl"] ??
            "https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5";
        if (!Uri.TryCreate(checkout, UriKind.Absolute, out var checkoutUrl))
        {
            throw new InvalidOperationException("Payment:ECPay:CheckoutUrl 必須是絕對網址。");
        }

        var initiationMinutes = configuration.GetValue("Payment:ECPay:InitiationLifetimeMinutes", 30);
        var callbackAgeMinutes = configuration.GetValue("Payment:ECPay:CallbackMaxAgeMinutes", 20);
        var allowSimulated = configuration.GetValue("Payment:ECPay:AllowSimulatedPaid", false);
        return (new EcpaySettings(
            merchantId,
            checkoutUrl,
            TimeSpan.FromMinutes(initiationMinutes),
            TimeSpan.FromMinutes(callbackAgeMinutes),
            allowSimulated), hashKey, hashIv);
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"缺少綠界設定 '{key}'。");
    }
}
