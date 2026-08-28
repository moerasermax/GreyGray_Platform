using System.Text.Json.Serialization;

namespace GreyGray.Modules.Payment.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(PaymentCaptured))]
[JsonSerializable(typeof(PaymentFailed))]
[JsonSerializable(typeof(PaymentRefunded))]
[JsonSerializable(typeof(PayoutSettled))]
[JsonSerializable(typeof(ReconciliationDiscrepancyFound))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
