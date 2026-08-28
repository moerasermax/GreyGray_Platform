using System.Text.Json.Serialization;

namespace GreyGray.Modules.Identity.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CustomerRegistered))]
[JsonSerializable(typeof(CustomerDeactivated))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
