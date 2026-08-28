using System.Text.Json.Serialization;

namespace GreyGray.Modules.Reporting.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ReportGenerated))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
