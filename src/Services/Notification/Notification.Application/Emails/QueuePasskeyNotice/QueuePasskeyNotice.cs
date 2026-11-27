using Auth.Contracts;
using Notification.Application.Abstractions;
using Notification.Domain.Emails;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.Application.Emails.QueuePasskeyNotice;

/// <summary>
/// Queues the notice that a passkey was added to or removed from the account: one each. Like the password notice, it
/// cannot be turned off: it is how the owner learns that someone else added a way in.
/// </summary>
public sealed record QueuePasskeyNoticeCommand(Guid UserId, Guid PasskeyId, string Name, bool Added, DateTimeOffset At) : ICommand;

internal sealed class QueuePasskeyNoticeHandler(IOutgoingEmailRepository emails, IEmailLinks links, TimeProvider timeProvider)
    : ICommandHandler<QueuePasskeyNoticeCommand>
{
    public async Task<Result> Handle(QueuePasskeyNoticeCommand command, CancellationToken cancellationToken)
    {
        var kind = command.Added ? EmailKind.PasskeyAdded : EmailKind.PasskeyRemoved;
        var sourceKey = command.PasskeyId.ToString("N");
        if (await emails.ExistsAsync(kind, sourceKey, cancellationToken).ConfigureAwait(false))
        {
            return Result.Success();
        }

        var security = links.To("account/security");
        var content = command.Added
            ? EmailTemplates.PasskeyAdded(command.Name, command.At, security)
            : EmailTemplates.PasskeyRemoved(command.Name, command.At, security);
        var email = OutgoingEmail.Queue(command.UserId, kind, sourceKey, content, timeProvider.GetUtcNow());
        if (email.IsFailure)
        {
            return email.Error;
        }

        emails.Add(email.Value);
        return Result.Success();
    }
}

/// <summary>
/// Passkey changes, from the account service. The full type name is this handler's inbox key: renaming or moving it
/// would process the messages it already handled again.
/// </summary>
internal sealed class QueuePasskeyNoticeOnPasskeyChanged(IDispatcher dispatcher)
    : IIntegrationEventHandler<PasskeyAdded>, IIntegrationEventHandler<PasskeyRemoved>
{
    public Task<Result> Handle(PasskeyAdded integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new QueuePasskeyNoticeCommand(integrationEvent.UserId, integrationEvent.PasskeyId, integrationEvent.Name, true, integrationEvent.AddedAt), cancellationToken);

    public Task<Result> Handle(PasskeyRemoved integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new QueuePasskeyNoticeCommand(integrationEvent.UserId, integrationEvent.PasskeyId, integrationEvent.Name, false, integrationEvent.RemovedAt), cancellationToken);
}
