using System.Text.Json.Serialization;

namespace GreyGray.Modules.Campaign.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CampaignPublished))]
[JsonSerializable(typeof(CampaignClosed))]
[JsonSerializable(typeof(TripCostRecorded))]
[JsonSerializable(typeof(CampaignCancelled))]
[JsonSerializable(typeof(CampaignSettled))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
