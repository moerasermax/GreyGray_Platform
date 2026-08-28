using System.Text.Json.Serialization;

namespace GreyGray.Modules.Ledger.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(JournalPosted))]
[JsonSerializable(typeof(LiabilityExceededCash))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
