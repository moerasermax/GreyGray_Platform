using System.Text.Json.Serialization;

namespace GreyGray.Modules.Ordering.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(OrderPlaced))]
[JsonSerializable(typeof(OrderPaid))]
[JsonSerializable(typeof(OrderReadyToShip))]
[JsonSerializable(typeof(OrderCompleted))]
[JsonSerializable(typeof(OrderCancelled))]
[JsonSerializable(typeof(OrderLineCancelled))]
[JsonSerializable(typeof(PaymentRequested))]
[JsonSerializable(typeof(RefundRequested))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
