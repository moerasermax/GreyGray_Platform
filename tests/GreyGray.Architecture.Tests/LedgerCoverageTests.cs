using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Fulfillment.Contracts;
using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Ledger.Infra;
using GreyGray.Modules.Ordering.Contracts;
using GreyGray.Modules.Payment.Contracts;
using GreyGray.Modules.Procurement.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Messaging;
using GreyGray.Platform.Observability;
using GreyGray.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.Architecture.Tests;

/// <summary>
/// 鎖住「<c>docs/02-事件與狀態機.md</c> §5 分錄對照表的每個階段都有人發、有人收」（#43）。
/// </summary>
/// <remarks>
/// <para>
/// #43 的根因跟 #41 是同一種形狀：<b>文件寫了、型別有了、沒有人接上、沒有測試守住</b>。
/// <c>inventory.StockCostAllocated.v1</c> 的型別、XML doc、<c>EventTypeRegistry</c> 登記
/// 全都在，但全 repo 沒有任何 <c>IIntegrationEventHandler&lt;StockCostAllocated&gt;</c>；
/// <c>fulfillment.ShipmentDispatched.v1</c> 更糟——事件真的有發，只是 Ledger 沒訂閱，
/// 而<b>沒有 handler 的事件會被 <c>OutboxDispatcher</c> 直接標成已處理</b>。
/// 兩邊都不會有任何一條測試變紅，帳就這樣安靜地少了一整個階段。
/// </para>
/// <para>
/// 所以這裡的斷言不是「handler 解得出來」而已，而是<b>逐一點名分錄表列到的事件</b>：
/// 清單為空要紅，某一個事件在 Ledger 找不到 handler 也要紅，而且錯誤訊息要直接說出
/// 是哪一個階段掉了。
/// </para>
/// </remarks>
public sealed class LedgerCoverageTests
{
    private const string FakeConnectionString =
        "Host=127.0.0.1;Port=5432;Database=greygray_ledger_coverage_probe;Username=probe;Password=probe";

    /// <summary>跟 <see cref="WorkerCompositionTests"/> 同一份 13 個 schema 清單。</summary>
    private static readonly string[] ModuleSchemas =
    [
        "iam", "catalog", "campaign", "pricing", "inventory", "checkout", "ordering",
        "procurement", "fulfillment", "payment", "ledger", "notify", "platform",
    ];

    /// <summary>
    /// <c>docs/02-事件與狀態機.md</c> 第 165-175 行的分錄對照表，逐列抄成型別。
    /// 表上多一列，這裡就要多一個——這份清單就是「帳該有幾段」的可執行版本。
    /// </summary>
    public static TheoryData<Type, string> LedgerEventTypes() => new()
    {
        { typeof(PaymentCaptured), "③ 客人付清：DR 綠界在途 / CR 預收貨款 · 預收運費" },
        { typeof(PaymentRefunded), "⑤ 缺貨退款：DR 預收貨款 / CR 客戶儲值金或原路" },
        { typeof(GoodsReceived), "⑤ 現場刷卡買入：DR 存貨 / CR 現金" },
        { typeof(LotCreated), "— 本地批發進貨建立批號：DR 存貨 / CR 現金" },
        { typeof(TripCostRecorded), "⑥ 旅程成本：DR 旅程成本 / CR 現金" },
        { typeof(StockCostAllocated), "⑦ 出貨從批號結轉：DR 銷貨成本 / CR 存貨" },
        { typeof(ShipmentDispatched), "⑦ 支付宅配運費：DR 運費成本 / CR 現金" },
        { typeof(OrderCompleted), "⑧ 完成，預收轉收入：DR 預收 / CR 收入" },
        { typeof(PayoutSettled), "— 綠界撥款到帳：DR 現金 · 手續費 / CR 綠界在途" },
    };

    [Fact(DisplayName = "分錄表的清單本身不能是空的——查了零個對象跟查過都沒事長得一樣")]
    public void Ledger_event_list_is_not_empty()
    {
        LedgerEventTypes().Count.ShouldBe(
            9,
            "docs/02 §5 分錄對照表現在是 9 列（含兩列沒有階段編號的）。"
            + "表改了就要一起改這裡，不要只改一邊。");
    }

    [Theory(DisplayName = "docs/02 §5 分錄表的每個事件，Ledger 都要有 handler 接")]
    [MemberData(nameof(LedgerEventTypes))]
    public void Every_ledger_table_event_has_a_ledger_handler(Type eventType, string stage)
    {
        using var provider = BuildWorkerProvider();
        using var scope = provider.CreateScope();

        var handlerServiceType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var handlers = scope.ServiceProvider.GetServices(handlerServiceType).ToArray();
        handlers.ShouldNotBeEmpty(
            $"{eventType.Name} 一個 handler 都沒有——分錄表的「{stage}」整段會消失，"
            + "而且沒有 handler 的事件會被 OutboxDispatcher 直接標成已處理，不會重試也不會告警。");

        var ledgerAssembly = typeof(LedgerModuleRegistration).Assembly;
        var handledByLedger = handlers.Any(handler =>
            InnerHandlerType(handler!.GetType())?.Assembly == ledgerAssembly);
        handledByLedger.ShouldBeTrue(
            $"{eventType.Name} 有人訂閱，但沒有一個來自 Ledger.Infra——"
            + $"分錄表的「{stage}」還是不會入帳。"
            + $"目前訂閱者：{string.Join(", ", handlers.Select(handler => InnerHandlerType(handler!.GetType())?.Name ?? handler.GetType().Name))}。");
    }

    [Fact(DisplayName =
        "★ 負向對照：ShipmentDispatched 也有非 Ledger 的訂閱者——「有人接」不等於「Ledger 接了」")]
    public void Having_any_subscriber_is_not_the_same_as_having_a_ledger_subscriber()
    {
        // 上面那條的第二個斷言（handledByLedger）如果跟第一個（ShouldNotBeEmpty）等價，
        // 它就是一條永遠不會紅的裝飾。ShipmentDispatched 正好是反例：Ordering 也訂閱它
        // （把品項推到 Shipped，#42），所以就算 Ledger 完全沒接，第一個斷言仍然會過。
        using var provider = BuildWorkerProvider();
        using var scope = provider.CreateScope();

        var handlers = scope.ServiceProvider
            .GetServices<IIntegrationEventHandler<ShipmentDispatched>>()
            .ToArray();
        var ledgerAssembly = typeof(LedgerModuleRegistration).Assembly;
        var byLedger = handlers
            .Where(handler => InnerHandlerType(handler.GetType())?.Assembly == ledgerAssembly)
            .ToArray();
        var notByLedger = handlers
            .Where(handler => InnerHandlerType(handler.GetType())?.Assembly != ledgerAssembly)
            .ToArray();

        byLedger.ShouldNotBeEmpty("Ledger 必須訂閱 ShipmentDispatched（運費成本）。");
        notByLedger.ShouldNotBeEmpty(
            "ShipmentDispatched 應該同時有非 Ledger 的訂閱者（Ordering 的品項狀態）；"
            + "少了它，上面那條測試的兩個斷言就變成同一件事。");
    }

    [Fact(DisplayName =
        "★ StockCostAllocated 要有發布端——事件型別定義了卻沒人發，帳一樣少一段")]
    public void Stock_cost_allocated_has_a_publisher_inside_inventory()
    {
        // #43 的另一半：`inventory.StockCostAllocated.v1` 的型別、XML doc、EventTypeRegistry
        // 登記全都在，`Every_ledger_table_event_has_a_ledger_handler` 也會過（Ledger 有訂閱），
        // 但全 repo 沒有任何地方 `new StockCostAllocated(` ——事件永遠不會發生，
        // 所以銷貨成本永遠不會結轉，而且沒有任何一條測試會紅。
        //
        // 用原始碼掃描而不是反射：「有沒有人建構這個事件」不是型別系統看得到的事實。
        var inventoryDirectory = Path.Combine(
            FindRepositoryRoot(), "src", "Modules", "Inventory");
        Directory.Exists(inventoryDirectory).ShouldBeTrue(
            $"找不到 {inventoryDirectory}——這條測試在掃一個不存在的目錄，"
            + "「查了零個檔案」會看起來跟「查過都沒事」一樣。");

        var sources = Directory
            .EnumerateFiles(inventoryDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            .ToArray();
        sources.ShouldNotBeEmpty($"{inventoryDirectory} 底下一個 .cs 都沒有，掃描沒有意義。");

        var publishers = sources
            .Where(path => File.ReadAllText(path)
                .Contains($"new {nameof(StockCostAllocated)}(", StringComparison.Ordinal))
            .ToArray();
        publishers.ShouldNotBeEmpty(
            $"掃了 {sources.Length} 個檔案，Inventory 底下沒有任何一處 "
            + $"`new {nameof(StockCostAllocated)}(`——"
            + "事件型別定義了卻沒有人發，Ledger 那邊的 handler 就是一段死碼，"
            + "出貨的銷貨成本永遠不會結轉（#43 的另一半）。");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GreyGray.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Cannot locate GreyGray.slnx from test output directory.");
    }

    /// <summary>
    /// 解出來的實例是 <c>IdempotentIntegrationEventHandler&lt;TEvent, THandler, TDbContext&gt;</c>
    /// 這層 decorator，真正記帳的是第二個型別參數。不是 decorator 就直接回它自己。
    /// </summary>
    private static Type? InnerHandlerType(Type resolvedType) =>
        resolvedType.IsConstructedGenericType
        && resolvedType.GetGenericTypeDefinition() == typeof(IdempotentIntegrationEventHandler<,,>)
            ? resolvedType.GetGenericArguments()[1]
            : resolvedType;

    private static ServiceProvider BuildWorkerProvider()
    {
        var services = new ServiceCollection();
        services.AddGreyGrayRuntimeContext();
        services.AddWorkerModules(BuildConfiguration());
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// 逐字比照 <see cref="WorkerCompositionTests"/> 的 in-memory 設定：連線字串都是假的
    /// （模組的 DbContext 延後到真的查詢才連線），綠界三個鍵是 Payment 模組解析時要的，
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
            ["Payment:ECPay:CheckoutUrl"] = "https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5",
            ["Payment:ECPay:CreditDetailUrl"] = "https://payment-stage.ecpay.com.tw/CreditDetail/DoAction",
        };
        foreach (var schema in ModuleSchemas)
        {
            settings[$"ConnectionStrings:GreyGray_{schema}"] = FakeConnectionString;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }
}
