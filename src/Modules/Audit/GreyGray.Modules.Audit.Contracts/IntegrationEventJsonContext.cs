using System.Text.Json.Serialization;

namespace GreyGray.Modules.Audit.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AuditRecorded))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
