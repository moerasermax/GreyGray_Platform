using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Payment.Core;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Platform.Abstractions.Audit;
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
        services.AddHttpClient("Ecpay", client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IEcpayGateway>(provider =>
        {
            var (settings, hashKey, hashIv) = ReadEcpaySettings(configuration);
            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Ecpay");
            return new EcpayGateway(settings, hashKey, hashIv, httpClient);
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
        services.AddScoped<IEcpayCallbackVerifier>(provider =>
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
                provider.GetRequiredService<IEcpayGateway>(),
                provider.GetRequiredService<IClock>(),
                provider.GetService<IAuditWriter>());
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
        // BE-49：只填三個憑證值會讓正式金鑰安靜地打到測試站，網址必須由投遞者明確選擇。
        // docs/46「D3 開張資料基準與還原演練」要求還原設定且隔離對外副作用；不可用預設猜環境。
        var checkout = Required(configuration, "Payment:ECPay:CheckoutUrl",
            "正式站：https://payment.ecpay.com.tw/Cashier/AioCheckOut/V5；" +
            "測試站：https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5。" +
            "這兩個網址刻意沒有預設，因為預設會讓正式金鑰安靜地打到測試站。");
        if (!Uri.TryCreate(checkout, UriKind.Absolute, out var checkoutUrl))
        {
            throw new InvalidOperationException("Payment:ECPay:CheckoutUrl 必須是絕對網址。");
        }

        // 退刷／關帳 API（DoAction）與 aio 導轉頁不同路徑，要另一個設定鍵。
        // 綠界官方文件明講測試環境「因無法提供實際授權，故無法使用此 API」——
        // stage 網址只是給簽章／串接層面驗證用，不代表能在 stage 真的退成功。
        var creditDetail = Required(configuration, "Payment:ECPay:CreditDetailUrl",
            "正式站：https://payment.ecpay.com.tw/CreditDetail/DoAction；" +
            "測試站串接設定：https://payment-stage.ecpay.com.tw/CreditDetail/DoAction" +
            "（綠界測試環境不支援實際授權／退刷）。" +
            "這兩個網址刻意沒有預設，因為預設會讓正式金鑰安靜地打到測試站。");
        if (!Uri.TryCreate(creditDetail, UriKind.Absolute, out var creditDetailUrl))
        {
            throw new InvalidOperationException("Payment:ECPay:CreditDetailUrl 必須是絕對網址。");
        }

        // ADR-029：dev 用的綠界模擬器（GreyGray.Tools.EcpaySimulator）就是靠上面那兩個網址接進來的。
        // 這道守衛的用意是「正式機忘了把 dev 設定拿掉時立刻炸」——不開旗標就只准打綠界自己的網域，
        // 而且只准 https。它在 DI 解析 IEcpayGateway／EcpaySettings 時執行，錯了會整條付款路徑 500，
        // 不會默默把真客人的錢導去別的地方。
        var allowNonEcpay = configuration.GetValue("Payment:ECPay:AllowNonEcpayEndpoints", false);
        if (!allowNonEcpay)
        {
            RequireEcpayEndpoint("Payment:ECPay:CheckoutUrl", checkoutUrl);
            RequireEcpayEndpoint("Payment:ECPay:CreditDetailUrl", creditDetailUrl);
        }

        var initiationMinutes = configuration.GetValue("Payment:ECPay:InitiationLifetimeMinutes", 30);
        var callbackAgeMinutes = configuration.GetValue("Payment:ECPay:CallbackMaxAgeMinutes", 20);
        var allowSimulated = configuration.GetValue("Payment:ECPay:AllowSimulatedPaid", false);
        return (new EcpaySettings(
            merchantId,
            checkoutUrl,
            creditDetailUrl,
            TimeSpan.FromMinutes(initiationMinutes),
            TimeSpan.FromMinutes(callbackAgeMinutes),
            allowSimulated), hashKey, hashIv);
    }

    /// <summary>
    /// 兩個綠界端點網址的網域守衛（ADR-029）。<b>只在 <c>Payment:ECPay:AllowNonEcpayEndpoints</c>
    /// 關著時執行</b>——它是「正式機忘了拿掉 dev 設定」的最後一道攔截，不是設定驗證的全部。
    /// </summary>
    private static void RequireEcpayEndpoint(string key, Uri value)
    {
        var host = value.Host;
        var isEcpayHost =
            string.Equals(host, "ecpay.com.tw", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".ecpay.com.tw", StringComparison.OrdinalIgnoreCase);
        if (value.Scheme == Uri.UriSchemeHttps && isEcpayHost)
        {
            return;
        }

        throw new InvalidOperationException(
            $"設定 '{key}' 目前是 '{value.AbsoluteUri}'，不是 https 的 ecpay.com.tw／*.ecpay.com.tw。" +
            "正式流程只准打綠界自己的網域；要指到本機模擬器或其他測試端點，" +
            "必須明確把 'Payment:ECPay:AllowNonEcpayEndpoints' 設成 true。");
    }

    private static string Required(IConfiguration configuration, string key, string guidance = "")
    {
        var value = configuration[key];
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"缺少綠界設定 '{key}'。" + guidance);
    }
}
