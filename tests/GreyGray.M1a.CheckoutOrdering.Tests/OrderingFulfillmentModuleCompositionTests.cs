using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Fulfillment.Infra;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Ordering.Infra;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Shared.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

/// <summary>
/// 迴歸測試（docs/24 §0）：admin Host（src/Hosts/GreyGray.Api.Admin/Program.cs）是全 repo
/// 唯一同時掛 Ordering 與 Fulfillment 兩個模組真實 DI 容器的地方，而這個組合曾經因為
/// OrderingApplicationService 的工廠用 GetService&lt;IFulfillmentQuery&gt;() 解析 Fulfillment、
/// FulfillmentApplicationService 的工廠又用 GetRequiredService&lt;IOrderQuery&gt;() 繞回 Ordering，
/// 形成建構時期循環相依，讓 /v1/orders、/v1/campaigns、/v1/shipments 永久掛住。
/// 修法是把 Ordering 對 Fulfillment 的依賴改成 Lazy 延遲解析；這條測試在修好之前
/// 應該在逾時內失敗，修好之後轉綠。
///
/// 實測發現：卡住的不只是解析本身，連 ServiceProvider／scope 的 DisposeAsync 都會卡在
/// 同一個內部鎖上（親自把修法暫時退回去驗證過：逾時後 `await using` 觸發的 Dispose
/// 讓整個測試執行檔卡死超過 60 秒，逼得用 taskkill 才收得掉）。所以這裡刻意不用
/// `await using`——容器與 scope 的建立、解析都關在背景執行緒裡，逾時就直接讓外層拋例外
/// 收工，不去碰任何可能被那條卡住的背景執行緒同時持有鎖的物件。
/// </summary>
public sealed class OrderingFulfillmentModuleCompositionTests
{
    [Fact(DisplayName = "admin Host 的組合（Ordering ＋ Fulfillment 同一個真實 DI 容器）解析不會卡死")]
    public async Task Resolving_ordering_and_fulfillment_together_does_not_deadlock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resolveTask = Task.Run(BuildAndResolve, cancellationToken);

        (IOrderingApplication Ordering, IFulfillmentQuery Fulfillment) resolved;
        try
        {
            resolved = await resolveTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                "解析 IOrderingApplication／IFulfillmentQuery 在 5 秒內沒有回來——" +
                "代表 Ordering ↔ Fulfillment 的建構時期循環相依又出現了（docs/24 §0），" +
                "不是單純變慢。admin Host 的 /v1/orders、/v1/campaigns、/v1/shipments 會因此永久掛住。",
                ex);
        }

        resolved.Ordering.ShouldNotBeNull();
        resolved.Fulfillment.ShouldNotBeNull();
    }

    /// <summary>
    /// 跟 admin Host 完全一樣的組合。刻意不 dispose 建出來的 provider／scope——
    /// 如果卡住，這個委派本身就永遠不會返回，容器只會靜靜留在記憶體裡到程序結束，
    /// 不會有任何一段程式碼去碰同一個可能卡死的內部鎖。
    /// </summary>
    private static (IOrderingApplication Ordering, IFulfillmentQuery Fulfillment) BuildAndResolve()
    {
        // 用假連線字串即可——OrderingDbContext／FulfillmentDbContext 在真的執行查詢之前
        // 不需要能連線（ModuleShapeTests.cs 已經是同樣的用法），這條測試只要證明
        // 「解析這兩個介面不會卡住」，不需要真的資料庫。
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GreyGray_ordering"] =
                    "Host=127.0.0.1;Database=model;Username=model;Password=model",
                ["ConnectionStrings:GreyGray_fulfillment"] =
                    "Host=127.0.0.1;Database=model;Username=model;Password=model",
            })
            .Build();

        var services = new ServiceCollection();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<ICorrelationContext>(new FakeCorrelation());
        services.AddSingleton<IPricingQuotation>(new FakePricing(clock));

        services.AddOrderingModule(configuration);
        services.AddFulfillmentModule(configuration);

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var ordering = scope.ServiceProvider.GetRequiredService<IOrderingApplication>();
        var fulfillment = scope.ServiceProvider.GetRequiredService<IFulfillmentQuery>();
        return (ordering, fulfillment);
    }
}
