using System.Text.Json.Serialization;

namespace GreyGray.Modules.Inventory.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(LotCreated))]
[JsonSerializable(typeof(StockReserved))]
[JsonSerializable(typeof(StockReleased))]
[JsonSerializable(typeof(StockCostAllocated))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
