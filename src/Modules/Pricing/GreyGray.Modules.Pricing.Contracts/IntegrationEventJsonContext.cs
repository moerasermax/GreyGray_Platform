using System.Text.Json.Serialization;

namespace GreyGray.Modules.Pricing.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(FeeRuleSetPublished))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
