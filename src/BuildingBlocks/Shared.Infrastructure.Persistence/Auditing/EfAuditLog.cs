using System.Diagnostics;
using System.Text.Json;
using Shared.Application.Auditing;

namespace Shared.Infrastructure.Persistence.Auditing;

/// <summary>Adds entries to the current context; they are written with the unit of work's save.</summary>
internal sealed class EfAuditLog<TContext>(TContext context, TimeProvider timeProvider) : IAuditLog
    where TContext : ServiceDbContext
{
    public void Add(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        context.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = timeProvider.GetUtcNow(),
            ActorId = entry.ActorId,
            Action = entry.Action,
            TargetId = entry.TargetId,

            // The runtime type: the details are usually a command held as an interface.
            Details = JsonSerializer.Serialize(entry.Details, entry.Details.GetType(), JsonSerializerOptions.Web),
            TraceId = Activity.Current?.TraceId.ToHexString(),
        });
    }
}
