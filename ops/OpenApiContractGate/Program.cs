using System.Text;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

return await ContractGate.RunAsync(args);

internal static class ContractGate
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = ParseArguments(args);
        if (!options.TryGetValue("expected", out var expectedPath) ||
            !options.TryGetValue("actual", out var actualPath) ||
            !options.TryGetValue("name", out var name))
        {
            Console.Error.WriteLine(
                "用法：--expected <凍結 yaml> --actual <AddOpenApi json> --name <文件名稱> " +
                "[--milestone M1a --catalog <docs/05-API契約.md>]");
            return 2;
        }

        var hasMilestone = options.TryGetValue("milestone", out var milestone);
        var hasCatalog = options.TryGetValue("catalog", out var catalogPath);
        if (hasMilestone != hasCatalog)
        {
            Console.Error.WriteLine("--milestone 與 --catalog 必須一起提供。");
            return 2;
        }

        try
        {
            var actual = await LoadAsync(actualPath);
            var actualPaths = actual.Paths?.Keys.Order(StringComparer.Ordinal).ToArray() ?? [];

            if (actualPaths.Length == 0 || actualPaths.All(path => !path.StartsWith("/v1/", StringComparison.Ordinal)))
            {
                Console.Error.WriteLine(
                    $"[{name}] FAIL-FAST：Host 尚未產出任何 /v1 契約（目前：{string.Join(", ", actualPaths)}）。" +
                    "不可把只有 /health 或空 schema 當成契約已同步；端點接線後此 gate 會自動進入完整差異比對。");
                return 1;
            }

            var expected = await LoadAsync(expectedPath);
            var expectedPaths = expected.Paths?.Keys.Order(StringComparer.Ordinal).ToArray() ?? [];
            Console.WriteLine($"[{name}] 凍結 paths={expectedPaths.Length}，AddOpenApi paths={actualPaths.Length}");

            if (hasMilestone)
            {
                return CompareMilestoneCoverage(
                    name,
                    milestone!,
                    catalogPath!,
                    expected,
                    actual);
            }

            var expectedJson = await expected.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1);
            var actualJson = await actual.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1);
            var expectedCanonical = Canonicalize(JsonNode.Parse(expectedJson));
            var actualCanonical = Canonicalize(JsonNode.Parse(actualJson));

            if (JsonNode.DeepEquals(expectedCanonical, actualCanonical))
            {
                Console.WriteLine($"[{name}] PASS：AddOpenApi 產物與凍結契約語意一致。");
                return 0;
            }

            Console.Error.WriteLine($"[{name}] FAIL：AddOpenApi 產物與凍結契約不同。");
            PrintPathSetDifference(expectedPaths, actualPaths);
            PrintFirstDifference(expectedCanonical, actualCanonical, "$", 0);
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[{name}] 無法完成契約比對：{exception.Message}");
            return 2;
        }
    }

    private static int CompareMilestoneCoverage(
        string name,
        string milestone,
        string catalogPath,
        OpenApiDocument expected,
        OpenApiDocument actual)
    {
        var catalog = ReadCatalog(catalogPath, name);
        var catalogOperations = catalog.Select(entry => entry.Operation).ToHashSet();
        var milestoneOperations = catalog
            .Where(entry => StringComparer.OrdinalIgnoreCase.Equals(entry.Milestone, milestone))
            .Select(entry => entry.Operation)
            .ToHashSet();
        if (milestoneOperations.Count == 0)
        {
            throw new InvalidDataException(
                $"{catalogPath} 的 {name} 表沒有任何里程碑 {milestone} operation。");
        }

        var frozenOperations = OperationsOf(expected);
        var actualOperations = OperationsOf(actual);
        var catalogMissingFromFrozen = catalogOperations.Except(frozenOperations).Order().ToArray();
        var frozenMissingFromCatalog = frozenOperations
            .Where(operation => operation.Path.StartsWith("/v1/", StringComparison.Ordinal))
            .Except(catalogOperations)
            .Order()
            .ToArray();
        var actualMissingFromCatalog = actualOperations
            .Where(operation => operation.Path.StartsWith("/v1/", StringComparison.Ordinal))
            .Except(catalogOperations)
            .Order()
            .ToArray();
        var missingMilestone = milestoneOperations.Except(actualOperations).Order().ToArray();

        Console.WriteLine(
            $"[{name}] {milestone} catalog operations={milestoneOperations.Count}，" +
            $"live covered={milestoneOperations.Count - missingMilestone.Length}");
        var failed = false;
        failed |= PrintOperationDifference("索引有、凍結契約沒有", catalogMissingFromFrozen);
        failed |= PrintOperationDifference("凍結契約有、索引未分類", frozenMissingFromCatalog);
        failed |= PrintOperationDifference("live 有、索引未分類", actualMissingFromCatalog);
        failed |= PrintOperationDifference($"live 缺少 {milestone}", missingMilestone);
        if (failed)
        {
            Console.Error.WriteLine(
                $"[{name}] FAIL：{milestone} operation coverage 不完整；本模式未執行完整 schema 語意比對。");
            return 1;
        }

        Console.WriteLine(
            $"[{name}] PASS：{milestone} operation coverage 完整；本模式未執行完整 schema 語意比對。");
        return 0;
    }

    private static IReadOnlyList<CatalogEntry> ReadCatalog(string path, string name)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("找不到端點里程碑索引。", path);
        }

        var targetHeading = name.Equals("storefront", StringComparison.OrdinalIgnoreCase)
            ? "### Storefront"
            : name.Equals("admin", StringComparison.OrdinalIgnoreCase)
                ? "### Admin"
                : throw new InvalidDataException(
                    $"里程碑索引只支援 storefront/admin，收到 name={name}。");
        var inTarget = false;
        var result = new List<CatalogEntry>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                inTarget = line.StartsWith(targetHeading, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inTarget || !line.StartsWith("| `", StringComparison.Ordinal))
            {
                continue;
            }

            var columns = line.Split('|')
                .Skip(1)
                .SkipLast(1)
                .Select(column => column.Trim().Trim('`'))
                .ToArray();
            var milestoneIndex = name.Equals("admin", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
            if (columns.Length <= milestoneIndex)
            {
                throw new InvalidDataException($"{path} 的 {name} 端點列欄位不足：{line}");
            }

            result.Add(new CatalogEntry(
                new ApiOperation(columns[0].ToUpperInvariant(), columns[1]),
                columns[milestoneIndex]));
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException($"{path} 找不到 {targetHeading} 端點列。");
        }

        var duplicate = result.GroupBy(entry => entry.Operation)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"{path} 重複列出 operation：{duplicate.Key}。");
        }

        return result;
    }

    private static HashSet<ApiOperation> OperationsOf(OpenApiDocument document)
    {
        var result = new HashSet<ApiOperation>();
        if (document.Paths is null)
        {
            return result;
        }

        foreach (var (path, item) in document.Paths)
        {
            if (item?.Operations is not { } operations)
            {
                continue;
            }

            foreach (var method in operations.Keys)
            {
                result.Add(new ApiOperation(method.ToString().ToUpperInvariant(), path));
            }
        }

        return result;
    }

    private static bool PrintOperationDifference(string label, IReadOnlyCollection<ApiOperation> operations)
    {
        if (operations.Count == 0)
        {
            return false;
        }

        Console.Error.WriteLine($"  {label}（前 20）：{string.Join(", ", operations.Take(20))}");
        return true;
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                return new Dictionary<string, string>();
            }

            result[args[index][2..]] = args[index + 1];
        }
        return result;
    }

    private static async Task<OpenApiDocument> LoadAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("找不到 OpenAPI 文件。", path);
        }

        await using var stream = File.OpenRead(path);
        var format = Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) ? "json" : "yaml";
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();
        var (document, diagnostic) = await OpenApiDocument.LoadAsync(stream, format, settings);
        if (document is null)
        {
            var details = diagnostic is null
                ? "沒有 diagnostic"
                : string.Join(" | ", diagnostic.Errors.Select(error => error.Message));
            throw new InvalidDataException($"{path} 解析後沒有 OpenApiDocument：{details}");
        }

        if (diagnostic is not null && diagnostic.Errors.Count > 0)
        {
            throw new InvalidDataException(
                $"{path} 有 OpenAPI 解析錯誤：{string.Join(" | ", diagnostic.Errors.Select(error => error.Message))}");
        }

        return document;
    }

    private static JsonNode? Canonicalize(JsonNode? node)
    {
        return node switch
        {
            JsonObject jsonObject => new JsonObject(
                jsonObject.OrderBy(property => property.Key, StringComparer.Ordinal)
                    .Select(property => KeyValuePair.Create(property.Key, Canonicalize(property.Value)))),
            JsonArray jsonArray => new JsonArray(jsonArray.Select(Canonicalize).ToArray()),
            null => null,
            _ => JsonNode.Parse(node.ToJsonString())
        };
    }

    private static void PrintPathSetDifference(string[] expected, string[] actual)
    {
        var missing = expected.Except(actual, StringComparer.Ordinal).ToArray();
        var unexpected = actual.Except(expected, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            Console.Error.WriteLine($"  缺少 paths（前 20）：{string.Join(", ", missing.Take(20))}");
        }
        if (unexpected.Length > 0)
        {
            Console.Error.WriteLine($"  多出 paths（前 20）：{string.Join(", ", unexpected.Take(20))}");
        }
    }

    private static bool PrintFirstDifference(JsonNode? expected, JsonNode? actual, string pointer, int depth)
    {
        if (JsonNode.DeepEquals(expected, actual)) { return false; }
        if (depth > 100)
        {
            Console.Error.WriteLine($"  第一個差異：{pointer}（超過遞迴深度）");
            return true;
        }

        if (expected is JsonObject expectedObject && actual is JsonObject actualObject)
        {
            foreach (var key in expectedObject.Select(item => item.Key)
                         .Union(actualObject.Select(item => item.Key), StringComparer.Ordinal)
                         .Order(StringComparer.Ordinal))
            {
                expectedObject.TryGetPropertyValue(key, out var expectedValue);
                actualObject.TryGetPropertyValue(key, out var actualValue);
                if (!JsonNode.DeepEquals(expectedValue, actualValue))
                {
                    return PrintFirstDifference(expectedValue, actualValue, $"{pointer}/{key}", depth + 1);
                }
            }
        }
        else if (expected is JsonArray expectedArray && actual is JsonArray actualArray)
        {
            var count = Math.Max(expectedArray.Count, actualArray.Count);
            for (var index = 0; index < count; index++)
            {
                var expectedValue = index < expectedArray.Count ? expectedArray[index] : null;
                var actualValue = index < actualArray.Count ? actualArray[index] : null;
                if (!JsonNode.DeepEquals(expectedValue, actualValue))
                {
                    return PrintFirstDifference(expectedValue, actualValue, $"{pointer}/{index}", depth + 1);
                }
            }
        }

        Console.Error.WriteLine($"  第一個差異：{pointer}");
        Console.Error.WriteLine($"    frozen: {FormatValue(expected)}");
        Console.Error.WriteLine($"    actual: {FormatValue(actual)}");
        return true;
    }

    private static string FormatValue(JsonNode? value)
    {
        if (value is null) { return "<missing-or-null>"; }
        var text = value.ToJsonString();
        return text.Length <= 500 ? text : text[..500] + "...";
    }

    private readonly record struct ApiOperation(string Method, string Path) : IComparable<ApiOperation>
    {
        public int CompareTo(ApiOperation other)
        {
            var path = StringComparer.Ordinal.Compare(Path, other.Path);
            return path != 0 ? path : StringComparer.Ordinal.Compare(Method, other.Method);
        }

        public override string ToString() => $"{Method} {Path}";
    }

    private sealed record CatalogEntry(ApiOperation Operation, string Milestone);
}
