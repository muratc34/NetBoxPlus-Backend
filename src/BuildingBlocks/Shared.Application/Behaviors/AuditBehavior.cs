using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Shared.Application.Behaviors;

/// <summary>
/// Records a successful <see cref="IAuditedCommand"/> in the audit log. Runs inside the transaction
/// behavior, so the entry is saved with the change; a failed command records nothing.
/// </summary>
internal sealed class AuditBehavior<TRequest, TResponse>(IAuditLog auditLog, ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IAuditedCommand
    where TResponse : Result
{
    private const string CommandSuffix = "Command";

    private static string ActionName { get; } = typeof(TRequest).Name.EndsWith(CommandSuffix, StringComparison.Ordinal)
        ? typeof(TRequest).Name[..^CommandSuffix.Length]
        : typeof(TRequest).Name;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var result = await next(cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            var targetId = request.AuditTargetId ?? (result as Result<Guid>)?.Value;
            auditLog.Add(new AuditEntry(ActionName, currentUser.Id, targetId, request));
        }

        return result;
    }
}
