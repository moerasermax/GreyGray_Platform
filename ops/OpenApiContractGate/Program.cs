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
            Console.Error.WriteLine("用法：--expected <凍結 yaml> --actual <AddOpenApi json> --name <文件名稱>");
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
}
