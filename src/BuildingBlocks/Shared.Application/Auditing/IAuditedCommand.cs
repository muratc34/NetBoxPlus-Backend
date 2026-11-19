using Shared.Application.Cqrs;

namespace Shared.Application.Auditing;

/// <summary>
/// A command whose successful runs are written to the audit log: who, when, which command, on
/// which record, and the command itself as JSON. The command must therefore carry no secrets
/// (an architecture test rejects property names such as <c>Password</c> or <c>Token</c>).
/// </summary>
/// <remarks>
/// Implement <see cref="AuditTargetId"/> explicitly, so that it stays out of the recorded JSON.
/// </remarks>
public interface IAuditedCommand : ICommandBase
{
    /// <summary>
    /// The record the command changes. <see langword="null"/> for a command that creates it: the
    /// id returned in a <c>Result&lt;Guid&gt;</c> is recorded instead.
    /// </summary>
    Guid? AuditTargetId { get; }
}
