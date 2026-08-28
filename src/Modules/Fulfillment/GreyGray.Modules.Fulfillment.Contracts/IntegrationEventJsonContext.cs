using System.Text.Json.Serialization;

namespace GreyGray.Modules.Fulfillment.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ShipmentDispatched))]
[JsonSerializable(typeof(ShipmentDelivered))]
[JsonSerializable(typeof(ReturnReceived))]
[JsonSerializable(typeof(ShipmentLost))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
