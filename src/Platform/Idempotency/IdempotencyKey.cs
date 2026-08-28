namespace GreyGray.Platform.Idempotency;

internal sealed class IdempotencyKey
{
    public required string Key { get; init; }

    public required string Scope { get; init; }

    public required string RequestHash { get; set; }

    public required string Status { get; set; }

    public string? ResponseSnapshot { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
