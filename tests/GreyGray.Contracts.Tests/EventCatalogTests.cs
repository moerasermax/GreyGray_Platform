using System.Reflection;
using GreyGray.Platform.Abstractions.Messaging;
using Shouldly;
using Xunit;

namespace GreyGray.Contracts.Tests;

/// <summary>
/// 事件目錄的斷言。
/// </summary>
/// <remarks>
/// <b>重複的 <c>EventType</c> 會讓訊息被送到錯的 handler，而且症狀是隨機的。</b>
/// <c>IIntegrationEventTypeRegistry</c> 的實作要在啟動時擋下這件事，
/// 但等到啟動才發現太晚了——這一組測試讓它在 CI 就紅。
/// <para>
/// <b>組件載入的坑</b>（2026-08-28 踩過一次，記在這裡）：.NET 只在第一次用到某個組件的型別時
/// 才載入它。直接 <c>AppDomain.CurrentDomain.GetAssemblies()</c> 會漏掉還沒被碰過的 Contracts，
/// 測試就變成空跑通過——比沒有測試更糟。所以下面用具名的 marker type 強制載入，
/// 並斷言數量是 14：新增模組卻忘了加進來，這條會紅。
/// </para>
/// </remarks>
public sealed class EventCatalogTests
{
    /// <summary>每個模組挑一個型別當「把組件拉進來」的把手。</summary>
    private static readonly Type[] ModuleAnchors =
    [
        typeof(Modules.Identity.Contracts.CustomerId),
        typeof(Modules.Catalog.Contracts.SkuId),
        typeof(Modules.Campaign.Contracts.CampaignId),
        typeof(Modules.Pricing.Contracts.DeliveryMethod),
        typeof(Modules.Inventory.Contracts.LotId),
        typeof(Modules.Checkout.Contracts.CartId),
        typeof(Modules.Ordering.Contracts.OrderId),
        typeof(Modules.Procurement.Contracts.InquiryId),
        typeof(Modules.Fulfillment.Contracts.ShipmentId),
        typeof(Modules.Payment.Contracts.PaymentId),
        typeof(Modules.Ledger.Contracts.EntryId),
        typeof(Modules.Notification.Contracts.NotificationId),
        typeof(Modules.Audit.Contracts.AuditRecordId),
        typeof(Modules.Reporting.Contracts.ReportGenerated),
    ];

    /// <summary>模組 → 它的 schema 名。事件型別名的前綴必須是 schema，不是模組名。</summary>
    private static readonly Dictionary<string, string> SchemaOf = new(StringComparer.Ordinal)
    {
        ["Identity"] = "iam",
        ["Catalog"] = "catalog",
        ["Campaign"] = "campaign",
        ["Pricing"] = "pricing",
        ["Inventory"] = "inventory",
        ["Checkout"] = "checkout",
        ["Ordering"] = "ordering",
        ["Procurement"] = "procurement",
        ["Fulfillment"] = "fulfillment",
        ["Payment"] = "payment",
        ["Ledger"] = "ledger",
        ["Notification"] = "notify",
        ["Audit"] = "audit",
        ["Reporting"] = "reporting",
    };

    private static readonly IReadOnlyList<Type> EventTypes = DiscoverEventTypes();

    [Fact(DisplayName = "十四個模組的 Contracts 組件都真的被載入了")]
    public void All_fourteen_contract_assemblies_are_loaded()
    {
        // 這條是其他測試的前提。少載一個組件，底下的斷言就會空跑通過。
        ModuleAnchors.Length.ShouldBe(14);
        ModuleAnchors.Select(t => t.Assembly).Distinct().Count().ShouldBe(14);
    }

    [Fact(DisplayName = "事件數量與 docs/02 的事件目錄一致（44 個）")]
    public void Event_count_matches_the_catalog()
    {
        EventTypes.Count.ShouldBe(
            44,
            "新增或刪除事件時，docs/02-事件與狀態機.md 的事件目錄要同步更新——" +
            $"目前掃到 {EventTypes.Count} 個：{string.Join(", ", EventTypeNames())}");
    }

    [Fact(DisplayName = "EventType 不可重複——重複會讓訊息送到錯的 handler")]
    public void Event_types_are_unique()
    {
        var duplicates = EventTypeNames()
            .GroupBy(x => x, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        duplicates.ShouldBeEmpty(
            $"重複的事件型別名：{string.Join(", ", duplicates)}。" +
            "IIntegrationEventTypeRegistry 會在啟動時拋例外，但那太晚了。");
    }

    [Fact(DisplayName = "EventType 的格式是 {schema}.{Name}.v{n}，前綴要對得上模組的 schema")]
    public void Event_type_names_follow_the_convention()
    {
        foreach (var type in EventTypes)
        {
            var eventType = EventTypeOf(type);
            var parts = eventType.Split('.');

            parts.Length.ShouldBe(3, $"{type.Name} 的 EventType「{eventType}」不是三段式");
            parts[2].StartsWith('v').ShouldBeTrue(
                $"{type.Name} 的 EventType「{eventType}」少了版本後綴——" +
                "改名等於破壞契約，要改就發 .v2 並讓兩版並存一段時間");

            var module = ModuleNameOf(type);
            SchemaOf.ShouldContainKey(module);
            parts[0].ShouldBe(
                SchemaOf[module],
                $"{type.Name} 定義在 {module}，前綴應該是 schema「{SchemaOf[module]}」而不是「{parts[0]}」");
        }
    }

    [Fact(DisplayName = "每個事件都自己宣告了 AggregateType 與 AggregateId")]
    public void Every_event_declares_its_aggregate_identity()
    {
        // outbox_message 的這兩欄是 NOT NULL。漏填的後果是執行期 INSERT 失敗，
        // 而那時已經在交易裡了。IntegrationEventBase 把它們宣告成 abstract 就是為了
        // 讓編譯器擋下來——這條測試是第二道，防止有人把 base 改成非 abstract。
        foreach (var type in EventTypes)
        {
            const BindingFlags Declared =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            type.GetProperty("AggregateType", Declared).ShouldNotBeNull(
                $"{type.Name} 沒有自己宣告 AggregateType");
            type.GetProperty("AggregateId", Declared).ShouldNotBeNull(
                $"{type.Name} 沒有自己宣告 AggregateId");
        }
    }

    [Fact(DisplayName = "事件全部是 sealed record，且繼承 IntegrationEventBase")]
    public void Events_are_sealed_records_on_the_common_base()
    {
        foreach (var type in EventTypes)
        {
            type.IsSealed.ShouldBeTrue($"{type.Name} 不是 sealed——事件不該被繼承");
            typeof(IntegrationEventBase).IsAssignableFrom(type).ShouldBeTrue(
                $"{type.Name} 沒有繼承 IntegrationEventBase，共同欄位會少掉");
        }
    }

    private static IReadOnlyList<Type> DiscoverEventTypes()
    {
        var assemblies = ModuleAnchors.Select(t => t.Assembly).Distinct();

        return
        [
            .. assemblies
                .SelectMany(a => a.GetExportedTypes())
                .Where(t => t is { IsClass: true, IsAbstract: false }
                            && t.GetInterfaces().Contains(typeof(IIntegrationEvent)))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
        ];
    }

    private static IEnumerable<string> EventTypeNames() => EventTypes.Select(EventTypeOf);

    private static string EventTypeOf(Type type)
        => (string)type.GetProperty("EventType", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    private static string ModuleNameOf(Type type)
    {
        // GreyGray.Modules.Ordering.Contracts.OrderPlaced -> Ordering
        var parts = type.Namespace!.Split('.');
        return parts[^2];
    }
}
