using System.Text.Json.Serialization;

namespace GreyGray.Modules.Notification.Contracts;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(NotificationSent))]
[JsonSerializable(typeof(NotificationFailed))]
public sealed partial class IntegrationEventJsonContext : JsonSerializerContext;
