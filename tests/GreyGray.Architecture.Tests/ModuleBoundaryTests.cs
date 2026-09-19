using Shouldly;
using Xunit;

namespace GreyGray.Architecture.Tests;

/// <summary>
/// 模組硬邊界的斷言。<b>違規則測試失敗、CI 擋下</b>——
/// 這是把「低耦合」從口號變成可執行約束的關鍵。
/// </summary>
/// <remarks>
/// 斷言的對象是 <c>.csproj</c> 裡宣告的 ProjectReference，不是編譯後的組件參考。
/// 理由見 <see cref="ProjectGraph"/> 的註解——組件層級在模組還沒有程式碼時會空跑通過。
/// </remarks>
public sealed class ModuleBoundaryTests
{
    private static readonly string[] Modules =
    [
        "Identity", "Catalog", "Campaign", "Pricing", "Inventory", "Checkout", "Ordering",
        "Procurement", "Fulfillment", "Payment", "Ledger", "Notification", "Audit", "Reporting",
        "CustomerService",
    ];

    /// <summary>
    /// 支撐模組。規則是<b>不被任何業務模組依賴</b>——它們只訂閱事件。
    /// </summary>
    /// <remarks>
    /// 這是判斷邊界切得對不對的快速檢查：如果 Ordering 需要參考 Notification 才能發通知，
    /// 邊界就已經破了。<b>支撐模組之間可以互相發事件</b>（notify → Audit、
    /// Reporting → Notification），那不算破口，因為業務模組沒有因此被綁住（ADR-017）。
    /// </remarks>
    private static readonly string[] SupportModules =
    [
        "Notification", "Audit", "Reporting",
    ];

    private static readonly string[] Hosts =
    [
        "GreyGray.Api.Storefront", "GreyGray.Api.Admin", "GreyGray.Worker", "GreyGray.Tools.StaffBootstrap",
        // dev 用的綠界模擬器（ADR-029）。它跟其他組合根受同一條規則管：可以參考 *.Infra，
        // 不得直接參考任何模組的 *.Core。Web 那半（GreyGray.Tools.EcpaySimulator）只參考
        // 自己的 .Core 純函式層，對模組的相依集中在 .Core 一個地方。
        "GreyGray.Tools.EcpaySimulator", "GreyGray.Tools.EcpaySimulator.Core",
    ];

    /// <summary>Contracts 不該碰到的套件。出現任何一個都代表持久層或 Web 相依漏進了公開契約。</summary>
    private static readonly string[] ForbiddenInContracts =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Design",
        "Npgsql",
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "Microsoft.AspNetCore.OpenApi",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.DependencyInjection",
        "Testcontainers.PostgreSql",
    ];

    private static readonly ProjectGraph Graph = ProjectGraph.Load();

    [Fact(DisplayName = "十五個模組各有 Contracts / Core / Infra 三個專案")]
    public void All_modules_have_three_projects()
    {
        foreach (var module in Modules)
        {
            foreach (var layer in new[] { "Contracts", "Core", "Infra" })
            {
                Graph.References.ShouldContainKey(
                    $"GreyGray.Modules.{module}.{layer}",
                    $"少了 {module}.{layer}——這個測試靠專案存在才有意義，缺一個就有一塊沒被守住。");
            }
        }
    }

    [Fact(DisplayName = "Contracts 不得參考任何 Core 或 Infra")]
    public void Contracts_must_not_reference_core_or_infra()
    {
        foreach (var module in Modules)
        {
            var self = $"GreyGray.Modules.{module}.Contracts";

            Refs(self).Where(IsCore).ShouldBeEmpty($"{self} 參考了某個 Core");
            Refs(self).Where(IsInfra).ShouldBeEmpty($"{self} 參考了某個 Infra");
        }
    }

    [Fact(DisplayName = "Core 不得參考其他模組的 Core")]
    public void Core_must_not_reference_other_modules_core()
    {
        foreach (var module in Modules)
        {
            var self = $"GreyGray.Modules.{module}.Core";
            var offending = Refs(self).Where(IsCore).Where(r => r != self).ToArray();

            offending.ShouldBeEmpty(
                $"{self} 參考了 {string.Join(", ", offending)}。" +
                "跨模組只能經由 *.Contracts——Core 裡的型別全部是 internal，" +
                "這條由編譯器與這個測試雙重把關。");
        }
    }

    [Fact(DisplayName = "Core 不得參考任何 Infra")]
    public void Core_must_not_reference_any_infra()
    {
        foreach (var module in Modules)
        {
            var self = $"GreyGray.Modules.{module}.Core";

            Refs(self).Where(IsInfra).ShouldBeEmpty(
                $"{self} 參考了 Infra——相依方向反了，應該是 Infra 依賴 Core。");
        }
    }

    [Fact(DisplayName = "Infra 只能參考自己模組的 Core 與 Contracts")]
    public void Infra_must_only_reference_its_own_core()
    {
        foreach (var module in Modules)
        {
            var self = $"GreyGray.Modules.{module}.Infra";
            var offending = Refs(self)
                .Where(r => IsCore(r) || IsInfra(r))
                .Where(r => r != $"GreyGray.Modules.{module}.Core")
                .ToArray();

            offending.ShouldBeEmpty(
                $"{self} 參考了 {string.Join(", ", offending)}。" +
                "禁止跨 schema JOIN，沒有例外——Infra 之間互相參考就是那件事的第一步。");
        }
    }

    [Fact(DisplayName = "Host 不得直接參考任何 Core")]
    public void Hosts_must_not_reference_core_directly()
    {
        // 註：遞移到 Core 是必然的（Host → Infra → Core），擋不了也不該擋。
        // 真正的防線是 Core 裡的型別全部 internal——Host 看得到組件，用不到型別。
        foreach (var host in Hosts)
        {
            Refs(host).Where(IsCore).ShouldBeEmpty(
                $"{host} 直接參考了某個模組的 Core。" +
                "組合根只能呼叫 *.Infra 公開的 Add*Module() 擴充方法。");
        }
    }

    [Fact(DisplayName = "業務模組不得依賴支撐模組（Notification / Audit / Reporting）")]
    public void Business_modules_must_not_depend_on_support_modules()
    {
        var businessModules = Modules.Except(SupportModules, StringComparer.Ordinal).ToArray();

        foreach (var module in businessModules)
        {
            foreach (var layer in new[] { "Contracts", "Core", "Infra" })
            {
                var self = $"GreyGray.Modules.{module}.{layer}";
                var offending = Refs(self).Where(IsSupportModuleProject).ToArray();

                offending.ShouldBeEmpty(
                    $"{self} 參考了 {string.Join(", ", offending)}。" +
                    "支撐模組只訂閱事件，不被任何業務模組依賴——" +
                    "「Ordering 需要參考 Notification 才能發通知」就是邊界已經破了的樣子。" +
                    "需要同步留痕請用 GreyGray.Platform.Abstractions 的 IAuditWriter（ADR-017）；" +
                    "需要通知客人就發事件，讓 Notification 自己訂。");
            }
        }
    }

    [Fact(DisplayName = "Contracts 不得宣告持久層或 Web 的套件相依")]
    public void Contracts_must_not_declare_infrastructure_packages()
    {
        foreach (var module in Modules)
        {
            var self = $"GreyGray.Modules.{module}.Contracts";
            var offending = Packages(self)
                .Where(p => ForbiddenInContracts.Contains(p, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            offending.ShouldBeEmpty(
                $"{self} 宣告了 {string.Join(", ", offending)}。" +
                "Contracts 是別人唯一能參考的組件，它把基礎設施相依傳染出去就沒完了。" +
                "只擋 ProjectReference 擋不住這條——所以這個測試看的是 PackageReference。");
        }

        Packages("GreyGray.Platform.Abstractions")
            .Where(p => ForbiddenInContracts.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ShouldBeEmpty("Platform.Abstractions 是全專案相依最廣的組件，必須保持零基礎設施相依。");

        Packages("GreyGray.Shared.Kernel")
            .ShouldBeEmpty("Shared.Kernel 不得有任何套件相依，只用 BCL。");
    }

    [Fact(DisplayName = "Shared.Kernel 必須是相依圖的葉子")]
    public void Shared_kernel_must_be_a_leaf()
    {
        Refs("GreyGray.Shared.Kernel").ShouldBeEmpty(
            "Shared.Kernel 只放無業務語意的型別（Money、Currency、Result、IClock），" +
            "它不得參考本專案的任何其他組件。");
    }

    [Fact(DisplayName = "Platform.Abstractions 只能參考 Shared.Kernel")]
    public void Platform_abstractions_stays_thin()
    {
        Refs("GreyGray.Platform.Abstractions").ShouldBe(
            ["GreyGray.Shared.Kernel"],
            ignoreOrder: true,
            "Abstractions 一旦長出其他相依，所有 Contracts 都會跟著吃到——" +
            "它是全專案相依最廣的組件，必須保持乾淨。");
    }

    [Fact(DisplayName = "Contracts 不得（遞移地）碰到 EF Core 那一層")]
    public void Contracts_must_not_reach_persistence()
    {
        foreach (var module in Modules)
        {
            var self = $"GreyGray.Modules.{module}.Contracts";

            Graph.TransitiveReferencesOf(self).ShouldNotContain(
                "GreyGray.Platform",
                $"{self} 遞移參考到 GreyGray.Platform（帶 EF Core）。" +
                "Contracts 是別人唯一能參考的組件，它把持久層相依傳染出去就沒完了。" +
                "平台能力請用 GreyGray.Platform.Abstractions。");
        }
    }

    [Fact(DisplayName = "Contracts 之間的相依必須無環")]
    public void Contract_dependencies_must_be_acyclic()
    {
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var path = new List<string>();

        bool Visit(string node)
        {
            if (state.TryGetValue(node, out var s))
            {
                if (s != 1)
                {
                    return false;
                }

                path.Add(node);
                return true;
            }

            state[node] = 1;
            path.Add(node);

            foreach (var next in Refs(node).Where(IsContracts))
            {
                if (Visit(next))
                {
                    return true;
                }
            }

            state[node] = 2;
            path.RemoveAt(path.Count - 1);
            return false;
        }

        foreach (var module in Modules)
        {
            Visit($"GreyGray.Modules.{module}.Contracts").ShouldBeFalse(
                $"Contracts 出現循環參考：{string.Join(" -> ", path)}");
        }
    }

    private static IReadOnlyList<string> Refs(string project)
        => Graph.References.TryGetValue(project, out var refs)
            ? refs
            : throw new InvalidOperationException($"相依圖裡沒有專案 {project}——測試的前提壞了，不是通過。");

    private static IReadOnlyList<string> Packages(string project)
        => Graph.Packages.TryGetValue(project, out var packages)
            ? packages
            : throw new InvalidOperationException($"相依圖裡沒有專案 {project}——測試的前提壞了，不是通過。");

    private static bool IsSupportModuleProject(string name)
        => SupportModules.Any(m => name.StartsWith($"GreyGray.Modules.{m}.", StringComparison.Ordinal));

    private static bool IsCore(string name)
        => name.StartsWith("GreyGray.Modules.", StringComparison.Ordinal)
           && name.EndsWith(".Core", StringComparison.Ordinal);

    private static bool IsInfra(string name)
        => name.StartsWith("GreyGray.Modules.", StringComparison.Ordinal)
           && name.EndsWith(".Infra", StringComparison.Ordinal);

    private static bool IsContracts(string name)
        => name.StartsWith("GreyGray.Modules.", StringComparison.Ordinal)
           && name.EndsWith(".Contracts", StringComparison.Ordinal);
}
