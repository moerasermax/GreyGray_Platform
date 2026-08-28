using GreyGray.Shared.Kernel;

namespace GreyGray.Platform.Saga;

internal sealed class SagaTimer
{
    public Guid Id { get; init; }

    public TenantId TenantId { get; init; }

    public required string SagaType { get; init; }

    public required string SagaId { get; init; }

    public DateTimeOffset FireAt { get; init; }

    public required string Payload { get; init; }

    public DateTimeOffset? FiredAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}
