using System.Xml.Linq;

namespace GreyGray.Architecture.Tests;

/// <summary>
/// 直接解析 <c>.csproj</c> 建出專案相依圖。
/// </summary>
/// <remarks>
/// <b>為什麼不用 <c>Assembly.GetReferencedAssemblies()</c>：</b>
/// 編譯器會把「有宣告但程式碼沒用到」的參考從組件 manifest 裡裁掉。
/// 模組還沒有任何程式碼時，違規的 ProjectReference 因此看不出來——
/// 2026-08-28 實測：故意讓 Ordering.Core 參考 Ledger.Core，組件層級的斷言完全沒反應。
/// 專案檔是宣告的來源，它不會被裁掉，所以邊界規則必須在這一層斷。
/// </remarks>
internal sealed class ProjectGraph
{
    private ProjectGraph(
        IReadOnlyDictionary<string, IReadOnlyList<string>> references,
        IReadOnlyDictionary<string, IReadOnlyList<string>> packages,
        string repoRoot)
    {
        References = references;
        Packages = packages;
        RepoRoot = repoRoot;
    }

    /// <summary>專案名（不含 .csproj）→ 它直接參考的專案名。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> References { get; }

    /// <summary>
    /// 專案名 → 它宣告的 NuGet 套件名。
    /// </summary>
    /// <remarks>
    /// 為什麼要一起解析：只擋 ProjectReference 擋不住「Contracts 直接
    /// <c>PackageReference Microsoft.EntityFrameworkCore</c>」這種繞法。
    /// 那會讓持久層相依從 Contracts 傳染到每一個參考它的模組，
    /// 而 <c>Contracts_must_not_reach_persistence</c> 完全看不到。
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Packages { get; }

    public string RepoRoot { get; }

    public static ProjectGraph Load()
    {
        var repoRoot = FindRepoRoot();
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var packages = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var dir in new[] { "src", "tests" })
        {
            var root = Path.Combine(repoRoot, dir);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var doc = XDocument.Load(path);
                map[name] = ParseIncludes(doc, "ProjectReference", stripExtension: true);
                packages[name] = ParseIncludes(doc, "PackageReference", stripExtension: false);
            }
        }

        if (map.Count == 0)
        {
            throw new InvalidOperationException(
                $"在 {repoRoot} 底下找不到任何 csproj——架構測試會變成空跑通過，這比沒有測試更糟。");
        }

        return new ProjectGraph(map, packages, repoRoot);
    }

    /// <summary>某個專案「遞移」參考到的全部專案。</summary>
    public IReadOnlySet<string> TransitiveReferencesOf(string project)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(References.GetValueOrDefault(project, []));

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            foreach (var next in References.GetValueOrDefault(current, []))
            {
                stack.Push(next);
            }
        }

        return seen;
    }

    private static IReadOnlyList<string> ParseIncludes(XDocument doc, string elementName, bool stripExtension)
    {
        return
        [
            .. doc.Descendants(elementName)
                .Select(e => (string?)e.Attribute("Include"))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => stripExtension
                    ? Path.GetFileNameWithoutExtension(v!.Replace('\\', Path.DirectorySeparatorChar))
                    : v!.Trim())
        ];
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GreyGray.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"從 {AppContext.BaseDirectory} 往上找不到 GreyGray.slnx，無法定位 repo 根目錄。");
    }
}
