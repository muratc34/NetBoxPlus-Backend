using Auth.Contracts;
using Notification.Application.Abstractions;
using Notification.Domain.Emails;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.Application.Emails.QueuePasswordChanged;

/// <summary>
/// Queues the notice that the password changed: one per change. Sent however late it arrives, since
/// it is how the owner learns that someone else changed it.
/// </summary>
public sealed record QueuePasswordChangedCommand(Guid UserId, Guid ChangeId, DateTimeOffset ChangedAt) : ICommand;

internal sealed class QueuePasswordChangedHandler(IOutgoingEmailRepository emails, IEmailLinks links, TimeProvider timeProvider)
    : ICommandHandler<QueuePasswordChangedCommand>
{
    public async Task<Result> Handle(QueuePasswordChangedCommand command, CancellationToken cancellationToken)
    {
        var sourceKey = command.ChangeId.ToString("N");
        if (await emails.ExistsAsync(EmailKind.PasswordChanged, sourceKey, cancellationToken).ConfigureAwait(false))
        {
            return Result.Success();
        }

        var content = EmailTemplates.PasswordChanged(command.ChangedAt, links.To("forgot-password"));
        var email = OutgoingEmail.Queue(command.UserId, EmailKind.PasswordChanged, sourceKey, content, timeProvider.GetUtcNow());
        if (email.IsFailure)
        {
            return email.Error;
        }

        emails.Add(email.Value);
        return Result.Success();
    }
}

/// <summary>
/// The notice for every password change the account service announces. The full type name is this
/// handler's inbox key: renaming or moving it would process the messages it already handled again.
/// </summary>
internal sealed class QueuePasswordChangedOnChanged(IDispatcher dispatcher) : IIntegrationEventHandler<PasswordChanged>
{
    public Task<Result> Handle(PasswordChanged integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(
            new QueuePasswordChangedCommand(integrationEvent.UserId, integrationEvent.ChangeId, integrationEvent.ChangedAt),
            cancellationToken);
}
