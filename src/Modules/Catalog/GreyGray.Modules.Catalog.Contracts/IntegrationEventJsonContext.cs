using System.Text.Json.Serialization;

namespace GreyGray.Modules.Catalog.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SkuPublished))]
[JsonSerializable(typeof(SkuArchived))]
[JsonSerializable(typeof(SkuAttributesChanged))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
