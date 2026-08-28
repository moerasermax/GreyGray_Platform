using Shouldly;
using Xunit;

namespace Daigou.Architecture.Tests;

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
    ];

    private static readonly string[] Hosts =
    [
        "Daigou.Api.Storefront", "Daigou.Api.Admin", "Daigou.Worker",
    ];

    private static readonly ProjectGraph Graph = ProjectGraph.Load();

    [Fact(DisplayName = "十四個模組各有 Contracts / Core / Infra 三個專案")]
    public void All_modules_have_three_projects()
    {
        foreach (var module in Modules)
        {
            foreach (var layer in new[] { "Contracts", "Core", "Infra" })
            {
                Graph.References.ShouldContainKey(
                    $"Daigou.Modules.{module}.{layer}",
                    $"少了 {module}.{layer}——這個測試靠專案存在才有意義，缺一個就有一塊沒被守住。");
            }
        }
    }

    [Fact(DisplayName = "Contracts 不得參考任何 Core 或 Infra")]
    public void Contracts_must_not_reference_core_or_infra()
    {
        foreach (var module in Modules)
        {
            var self = $"Daigou.Modules.{module}.Contracts";

            Refs(self).Where(IsCore).ShouldBeEmpty($"{self} 參考了某個 Core");
            Refs(self).Where(IsInfra).ShouldBeEmpty($"{self} 參考了某個 Infra");
        }
    }

    [Fact(DisplayName = "Core 不得參考其他模組的 Core")]
    public void Core_must_not_reference_other_modules_core()
    {
        foreach (var module in Modules)
        {
            var self = $"Daigou.Modules.{module}.Core";
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
            var self = $"Daigou.Modules.{module}.Core";

            Refs(self).Where(IsInfra).ShouldBeEmpty(
                $"{self} 參考了 Infra——相依方向反了，應該是 Infra 依賴 Core。");
        }
    }

    [Fact(DisplayName = "Infra 只能參考自己模組的 Core 與 Contracts")]
    public void Infra_must_only_reference_its_own_core()
    {
        foreach (var module in Modules)
        {
            var self = $"Daigou.Modules.{module}.Infra";
            var offending = Refs(self)
                .Where(r => IsCore(r) || IsInfra(r))
                .Where(r => r != $"Daigou.Modules.{module}.Core")
                .ToArray();

            offending.ShouldBeEmpty(
                $"{self} 參考了 {string.Join(", ", offending)}。" +
                "禁止跨 schema JOIN，沒有例外——Infra 之間互相參考就是那件事的第一步。");
        }
    }

    [Fact(DisplayName = "Host 不得參考任何 Core（含遞移）")]
    public void Hosts_must_not_reference_core_directly()
    {
        foreach (var host in Hosts)
        {
            Refs(host).Where(IsCore).ShouldBeEmpty(
                $"{host} 直接參考了某個模組的 Core。" +
                "組合根只能呼叫 *.Infra 公開的 Add*Module() 擴充方法。");
        }
    }

    [Fact(DisplayName = "Shared.Kernel 必須是相依圖的葉子")]
    public void Shared_kernel_must_be_a_leaf()
    {
        Refs("Daigou.Shared.Kernel").ShouldBeEmpty(
            "Shared.Kernel 只放無業務語意的型別（Money、Currency、Result、IClock），" +
            "它不得參考本專案的任何其他組件。");
    }

    [Fact(DisplayName = "Platform.Abstractions 只能參考 Shared.Kernel")]
    public void Platform_abstractions_stays_thin()
    {
        Refs("Daigou.Platform.Abstractions").ShouldBe(
            ["Daigou.Shared.Kernel"],
            ignoreOrder: true,
            "Abstractions 一旦長出其他相依，所有 Contracts 都會跟著吃到——" +
            "它是全專案相依最廣的組件，必須保持乾淨。");
    }

    [Fact(DisplayName = "Contracts 不得（遞移地）碰到 EF Core 那一層")]
    public void Contracts_must_not_reach_persistence()
    {
        foreach (var module in Modules)
        {
            var self = $"Daigou.Modules.{module}.Contracts";

            Graph.TransitiveReferencesOf(self).ShouldNotContain(
                "Daigou.Platform",
                $"{self} 遞移參考到 Daigou.Platform（帶 EF Core）。" +
                "Contracts 是別人唯一能參考的組件，它把持久層相依傳染出去就沒完了。" +
                "平台能力請用 Daigou.Platform.Abstractions。");
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
            Visit($"Daigou.Modules.{module}.Contracts").ShouldBeFalse(
                $"Contracts 出現循環參考：{string.Join(" -> ", path)}");
        }
    }

    private static IReadOnlyList<string> Refs(string project)
        => Graph.References.TryGetValue(project, out var refs)
            ? refs
            : throw new InvalidOperationException($"相依圖裡沒有專案 {project}——測試的前提壞了，不是通過。");

    private static bool IsCore(string name)
        => name.StartsWith("Daigou.Modules.", StringComparison.Ordinal)
           && name.EndsWith(".Core", StringComparison.Ordinal);

    private static bool IsInfra(string name)
        => name.StartsWith("Daigou.Modules.", StringComparison.Ordinal)
           && name.EndsWith(".Infra", StringComparison.Ordinal);

    private static bool IsContracts(string name)
        => name.StartsWith("Daigou.Modules.", StringComparison.Ordinal)
           && name.EndsWith(".Contracts", StringComparison.Ordinal);
}
