namespace Shared.Infrastructure.Persistence.Auditing;

/// <summary>A change recorded in the audit log; written once, never updated.</summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    /// <summary><see langword="null"/> when the system made the change.</summary>
    public Guid? ActorId { get; init; }

    public required string Action { get; init; }

    public Guid? TargetId { get; init; }

    public required string Details { get; init; }

    public string? TraceId { get; init; }
}
