using System.Text.Json.Serialization;

namespace GreyGray.Modules.Checkout.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CheckoutCompleted))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
