using Notification.Application.Abstractions;
using Notification.Application.Emails;
using Notification.Domain.Emails;
using Notification.Domain.Recipients;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Notification.Application.Recipients.AddRecipient;

/// <summary>
/// Remembers where to write to a new user and queues their welcome e-mail. Coming again for the same
/// user is a success that changes nothing.
/// </summary>
public sealed record AddRecipientCommand(Guid UserId, string? Email) : ICommand;

internal sealed class AddRecipientHandler(IRecipientRepository recipients, IOutgoingEmailRepository emails, IEmailLinks links, TimeProvider timeProvider)
    : ICommandHandler<AddRecipientCommand>
{
    public async Task<Result> Handle(AddRecipientCommand command, CancellationToken cancellationToken)
    {
        var recipient = Recipient.Create(command.UserId, command.Email);
        if (recipient.IsFailure)
        {
            return recipient.Error;
        }

        // A second delivery under a different message id; the primary keys settle a race, which is retried.
        if (await recipients.ExistsAsync(command.UserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Success();
        }

        recipients.Add(recipient.Value);

        var sourceKey = command.UserId.ToString("N");
        if (!await emails.ExistsAsync(EmailKind.Welcome, sourceKey, cancellationToken).ConfigureAwait(false))
        {
            var welcome = OutgoingEmail.Queue(command.UserId, EmailKind.Welcome, sourceKey, EmailTemplates.Welcome(links.To(string.Empty)), timeProvider.GetUtcNow());
            emails.Add(welcome.Value);
        }

        return Result.Success();
    }
}
