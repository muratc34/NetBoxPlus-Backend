using Notification.Domain.Emails;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Notification.Application.Emails.RetryEmail;

/// <summary>Sends a failed e-mail again, from the first attempt, while its content is kept.</summary>
public sealed record RetryEmailCommand(Guid EmailId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => EmailId;
}

internal sealed class RetryEmailHandler(IOutgoingEmailRepository emails, TimeProvider timeProvider) : ICommandHandler<RetryEmailCommand>
{
    public async Task<Result> Handle(RetryEmailCommand command, CancellationToken cancellationToken)
    {
        var email = await emails.GetByIdAsync(command.EmailId, cancellationToken).ConfigureAwait(false);
        return email is null ? OutgoingEmailErrors.NotFound : email.Retry(timeProvider.GetUtcNow());
    }
}
