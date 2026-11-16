using Auth.Contracts;
using Notification.Application.Emails;
using Notification.Domain.Emails;
using Notification.Domain.Recipients;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.Application.Recipients.ForgetRecipient;

/// <summary>
/// The user's account was deleted: one last e-mail goes to the address they had, fixed to it, and then
/// the address is forgotten. E-mails still waiting are dropped and the content of the others emptied;
/// nothing is sent to the user afterwards. When this is the first news of the user, the recipient is
/// kept deleted, without an address, so that a late registration sends nothing. Coming again changes nothing.
/// </summary>
public sealed record ForgetRecipientCommand(Guid UserId, DateTimeOffset DeletedAt) : ICommand;

internal sealed class ForgetRecipientHandler(IRecipientRepository recipients, IOutgoingEmailRepository emails, TimeProvider timeProvider)
    : ICommandHandler<ForgetRecipientCommand>
{
    public async Task<Result> Handle(ForgetRecipientCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var recipient = await recipients.GetAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (recipient is null)
        {
            // Racing the registration here is settled by the primary key: the loser retries.
            var created = Recipient.Create(command.UserId, Recipient.DeletedEmail(command.UserId)).Value;
            created.Delete(command.DeletedAt);
            recipients.Add(created);
            return Result.Success();
        }

        if (recipient.DeletedAt is not null)
        {
            return Result.Success();
        }

        foreach (var email in await emails.GetForUserAsync(command.UserId, cancellationToken).ConfigureAwait(false))
        {
            if (email.Status == EmailStatus.Pending)
            {
                emails.Remove(email);
            }
            else
            {
                email.Forget();
            }
        }

        var goodbye = OutgoingEmail.Queue(
            command.UserId,
            EmailKind.AccountDeleted,
            command.UserId.ToString("N"),
            EmailTemplates.AccountDeleted(command.DeletedAt),
            now,
            recipient.Email);
        emails.Add(goodbye.Value);

        recipient.Delete(command.DeletedAt);
        return Result.Success();
    }
}

/// <summary>
/// Deleted accounts, from the account service. The full type name is this handler's inbox key:
/// renaming or moving it would process the messages it already handled again.
/// </summary>
internal sealed class ForgetRecipientOnUserDeleted(IDispatcher dispatcher) : IIntegrationEventHandler<UserDeleted>
{
    public Task<Result> Handle(UserDeleted integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new ForgetRecipientCommand(integrationEvent.UserId, integrationEvent.DeletedAt), cancellationToken);
}
