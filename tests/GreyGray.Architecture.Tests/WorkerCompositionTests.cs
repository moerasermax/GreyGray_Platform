using GreyGray.Modules.Campaign.Infra;
using GreyGray.Modules.Catalog.Infra;
using GreyGray.Modules.Checkout.Infra;
using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Identity.Infra;
using GreyGray.Modules.Inventory.Infra;
using GreyGray.Modules.Ledger.Infra;
using GreyGray.Modules.Notification.Infra;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Payment.Infra;
using GreyGray.Modules.Pricing.Infra;
using GreyGray.Modules.Procurement.Infra;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Observability;
using GreyGray.Shared.Kernel;
using GreyGray.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.Architecture.Tests;

/// <summary>
/// 鎖住 Worker 行程真的掛了哪些模組（#41）。
///
/// Worker 是唯一派送 Outbox 的行程，而 Ordering 的 <c>ShipmentDeliveredHandler</c> 對
/// <c>IFulfillmentQuery</c> 是 <c>Lazy</c> 相依，所以「缺 Fulfillment 模組」不會讓開機失敗、
/// 也不會讓任何既有測試變紅——它只在出貨單簽收事件真的派送到時炸
/// <c>ArgumentNullException</c>，訂單則永遠停在「準備出貨」。2026-09-03 正式機就是這樣。
///
/// 這裡呼叫的 <see cref="WorkerModules.AddWorkerModules"/> 就是 <c>Worker/Program.cs</c>
/// 用的同一個方法，不是抄一份清單——抄的那份會漂移，而漂移正是 #41 的成因。
/// 不需要 Postgres：模組的 DbContext 延後到真的查詢才連線，而
/// <c>RecordShipmentDeliveredAsync</c> 傳空清單時在碰資料庫之前就返回。
/// </summary>
public sealed class WorkerCompositionTests
{
    private const string FakeConnectionString =
        "Host=127.0.0.1;Port=5432;Database=greygray_model_probe;Username=probe;Password=probe";

    /// <summary>Worker 需要的 13 個 schema（跟 ops/start-dev-hosts.ps1、ops/deploy.ps1 同一份清單）。</summary>
    private static readonly string[] ModuleSchemas =
    [
        "iam", "catalog", "campaign", "pricing", "inventory", "checkout", "ordering",
        "procurement", "fulfillment", "payment", "ledger", "notify", "platform",
    ];

    [Fact(DisplayName = "Worker 的模組組合解得出每個事件 handler 的相依，並包含 Fulfillment")]
    public async Task Worker_composition_resolves_every_registered_event_handler()
    {
        var services = new ServiceCollection();
        services.AddGreyGrayRuntimeContext();
        services.AddWorkerModules(BuildConfiguration());

        var handlerTypes = ClosedIntegrationEventHandlerTypes(services);
        handlerTypes.ShouldNotBeEmpty(
            "查了零個 handler 跟查過都沒事長得一樣——Worker 一定有登記整合事件 handler。");

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        // ① 點名：Ordering 的 Lazy 相依只有點名才驗得到。
        scope.ServiceProvider.GetRequiredService<IFulfillmentQuery>().ShouldNotBeNull();

        // ② 通用：每個登記的 handler 都要解得出至少一個實例。
        foreach (var handlerType in handlerTypes)
        {
            var handlers = scope.ServiceProvider.GetServices(handlerType).ToArray();
            handlers.ShouldNotBeEmpty($"Worker 登記了 {handlerType} 卻解不出實例。");
        }

        // ③ 重現 #41 的炸點：正式機失敗的就是這條路徑（傳空清單不會碰資料庫）。
        var result = await scope.ServiceProvider
            .GetRequiredService<IOrderingShipmentDelivery>()
            .RecordShipmentDeliveredAsync(Array.Empty<OrderId>(), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact(DisplayName = "負向對照：Worker 少掛 Fulfillment 時，出貨簽收路徑必須明確失敗")]
    public async Task Worker_composition_without_fulfillment_fails_loudly()
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();
        services.AddGreyGrayRuntimeContext();

        // 這一串要跟 WorkerModules.AddWorkerModules 一致，只少 AddFulfillmentModule
        // ——也就是 2026-09-03 之前正式機 Worker 的真實組合。
        // 刻意不呼叫 AddWorkerModules 再移除 descriptor：Fulfillment 的註冊互相牽連，移不乾淨。
        services
            .AddIdentityModule(configuration)
            .AddCatalogModule(configuration)
            .AddCampaignModule(configuration)
            .AddPricingModule(configuration)
            .AddInventoryModule(configuration)
            .AddCheckoutModule(configuration)
            .AddOrderingModule(configuration)
            .AddProcurementModule(configuration)
            .AddPaymentModule(configuration)
            .AddLedgerModule(configuration)
            .AddNotificationModule(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => scope.ServiceProvider
                .GetRequiredService<IOrderingShipmentDelivery>()
                .RecordShipmentDeliveredAsync(Array.Empty<OrderId>(), TestContext.Current.CancellationToken));
        exception.Message.ShouldContain("Fulfillment");
    }

    /// <summary>
    /// 用 in-memory 設定餵齊註冊期／解析期會讀的鍵：連線字串都是假的（不會真的連），
    /// 綠界那三個鍵是 Payment 模組解析 IEcpayGateway／EcpaySettings 時要的，
    /// Identity 的金鑰要 32 bytes 的 Base64。
    /// </summary>
    private static IConfiguration BuildConfiguration()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:GreyGray_valkey"] = "127.0.0.1:6379",
            ["Identity:DataProtectionKey"] = Convert.ToBase64String(new byte[32]),
            ["Payment:ECPay:MerchantId"] = "2000132",
            ["Payment:ECPay:HashKey"] = "5294y06JbISpM5x9",
            ["Payment:ECPay:HashIV"] = "v77hoKGq4kWxNNIS",
        };
        foreach (var schema in ModuleSchemas)
        {
            settings[$"ConnectionStrings:GreyGray_{schema}"] = FakeConnectionString;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static Type[] ClosedIntegrationEventHandlerTypes(IServiceCollection services) =>
        services
            .Select(descriptor => descriptor.ServiceType)
            .Where(static type => type.IsConstructedGenericType
                && type.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>))
            .Distinct()
            .ToArray();
}
