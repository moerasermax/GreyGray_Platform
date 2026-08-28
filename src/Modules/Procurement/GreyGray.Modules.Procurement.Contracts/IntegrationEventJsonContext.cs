using System.Text.Json.Serialization;

namespace GreyGray.Modules.Procurement.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ItemPurchased))]
[JsonSerializable(typeof(GoodsReceived))]
[JsonSerializable(typeof(ItemUnavailable))]
[JsonSerializable(typeof(ItemPriceChanged))]
[JsonSerializable(typeof(InquiryResolved))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
