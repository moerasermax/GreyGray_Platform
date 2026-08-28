namespace GreyGray.Platform.Messaging;

/// <summary>記錄某個整合事件是否已由指定 handler 完成處理。</summary>
public sealed class ProcessedMessage
{
    public required Guid EventId { get; init; }

    public required string HandlerName { get; init; }

    public required DateTimeOffset ProcessedAt { get; init; }
}
