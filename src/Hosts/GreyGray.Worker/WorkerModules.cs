using GreyGray.Modules.Campaign.Infra;
using GreyGray.Modules.Catalog.Infra;
using GreyGray.Modules.Checkout.Infra;
using GreyGray.Modules.Fulfillment.Infra;
using GreyGray.Modules.Identity.Infra;
using GreyGray.Modules.Inventory.Infra;
using GreyGray.Modules.Ledger.Infra;
using GreyGray.Modules.Notification.Infra;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Payment.Infra;
using GreyGray.Modules.Pricing.Infra;
using GreyGray.Modules.Procurement.Infra;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GreyGray.Worker;

/// <summary>
/// Worker 掛哪些模組的**唯一事實來源**。<c>Program.cs</c> 與架構測試
/// <c>WorkerCompositionTests</c> 呼叫的是同一個方法，所以測試驗到的組合
/// 就是正式機真的跑的組合。
/// </summary>
/// <remarks>
/// #41 的成因就是這份清單原本只寫在 <c>Program.cs</c> 裡、沒有人測：
/// Worker 掛了 Ordering（含 <c>ShipmentDeliveredHandler</c>）卻沒掛 Fulfillment，
/// 而 handler 對 <c>IFulfillmentQuery</c> 是 <c>Lazy</c> 相依（Ordering.Infra/ModuleRegistration.cs），
/// 於是開機一切正常、直到出貨單簽收事件真的派送才炸 <c>ArgumentNullException</c>，
/// 訂單永遠停在「準備出貨」。
/// </remarks>
internal static class WorkerModules
{
    /// <summary>掛上 Worker 需要的 12 個模組。</summary>
    public static IServiceCollection AddWorkerModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services
            .AddIdentityModule(configuration)
            .AddCatalogModule(configuration)
            .AddCampaignModule(configuration)
            .AddPricingModule(configuration)
            .AddInventoryModule(configuration)
            .AddCheckoutModule(configuration)
            .AddOrderingModule(configuration)
            .AddProcurementModule(configuration)
            .AddPaymentModule(configuration)
            .AddFulfillmentModule(configuration)
            .AddLedgerModule(configuration)
            .AddNotificationModule(configuration);
    }
}
