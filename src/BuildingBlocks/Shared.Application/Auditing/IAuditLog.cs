namespace Shared.Application.Auditing;

/// <summary>One change made by a person or by the system.</summary>
/// <param name="Action">What was done, such as <c>AssignRole</c>.</param>
/// <param name="ActorId">The user who did it; <see langword="null"/> for the system itself (a seed, a migration).</param>
/// <param name="TargetId">The record it was done to, when there is one.</param>
/// <param name="Details">Recorded as JSON; must carry no secrets.</param>
public sealed record AuditEntry(string Action, Guid? ActorId, Guid? TargetId, object Details);

/// <summary>
/// Adds entries to the service's audit log. Like the outbox, nothing is written until the unit of
/// work saves, so an entry exists exactly when the change it describes does.
/// </summary>
public interface IAuditLog
{
    void Add(AuditEntry entry);
}
