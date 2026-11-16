using Auth.Contracts;
using Notification.Application.Abstractions;
using Notification.Application.Emails;
using Notification.Domain.Emails;
using Notification.Domain.Recipients;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.Application.Recipients.ChangeRecipientEmail;

/// <summary>
/// Moves the recipient to the new address and tells the old one: the notice is fixed to the address
/// it is sent to, since the recipient no longer has it. When this is the first news of the user, the
/// recipient is made with the new address and nobody is told; the registration that comes later then
/// only queues the welcome e-mail. An older or repeated change changes nothing.
/// </summary>
public sealed record ChangeRecipientEmailCommand(Guid UserId, string? Email, int Revision, DateTimeOffset ChangedAt) : ICommand;

internal sealed class ChangeRecipientEmailHandler(IRecipientRepository recipients, IOutgoingEmailRepository emails, IEmailLinks links, TimeProvider timeProvider)
    : ICommandHandler<ChangeRecipientEmailCommand>
{
    public async Task<Result> Handle(ChangeRecipientEmailCommand command, CancellationToken cancellationToken)
    {
        var recipient = await recipients.GetAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (recipient is null)
        {
            // Racing the registration here is settled by the primary key: the loser retries.
            var created = Recipient.Create(command.UserId, command.Email, command.Revision);
            if (created.IsFailure)
            {
                return created.Error;
            }

            recipients.Add(created.Value);
            return Result.Success();
        }

        var oldAddress = recipient.Email;
        var changed = recipient.ChangeEmail(command.Email, command.Revision);
        if (changed.IsFailure || !changed.Value)
        {
            return changed.IsFailure ? changed.Error : Result.Success();
        }

        var sourceKey = $"{command.UserId:N}-{command.Revision}";
        if (!await emails.ExistsAsync(EmailKind.EmailChanged, sourceKey, cancellationToken).ConfigureAwait(false))
        {
            var content = EmailTemplates.EmailChanged(recipient.Email, command.ChangedAt, links.To("forgot-password"));
            var notice = OutgoingEmail.Queue(command.UserId, EmailKind.EmailChanged, sourceKey, content, timeProvider.GetUtcNow(), oldAddress);
            emails.Add(notice.Value);
        }

        return Result.Success();
    }
}

/// <summary>
/// Keeps the recipient's address the user's. The full type name is this handler's inbox key: renaming
/// or moving it would process the messages it already handled again.
/// </summary>
internal sealed class ChangeRecipientEmailOnEmailChanged(IDispatcher dispatcher) : IIntegrationEventHandler<EmailChanged>
{
    public Task<Result> Handle(EmailChanged integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(
            new ChangeRecipientEmailCommand(integrationEvent.UserId, integrationEvent.Email, integrationEvent.Revision, integrationEvent.ChangedAt),
            cancellationToken);
}
