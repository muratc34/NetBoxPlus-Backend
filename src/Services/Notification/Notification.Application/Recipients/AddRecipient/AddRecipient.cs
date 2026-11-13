using Notification.Domain.Recipients;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Notification.Application.Recipients.AddRecipient;

/// <summary>Remembers where to write to a new user. Coming again for the same user is a success that changes nothing.</summary>
public sealed record AddRecipientCommand(Guid UserId, string? Email) : ICommand;

internal sealed class AddRecipientHandler(IRecipientRepository recipients) : ICommandHandler<AddRecipientCommand>
{
    public async Task<Result> Handle(AddRecipientCommand command, CancellationToken cancellationToken)
    {
        var recipient = Recipient.Create(command.UserId, command.Email);
        if (recipient.IsFailure)
        {
            return recipient.Error;
        }

        // A second delivery under a different message id; the primary key settles a race, which is retried.
        if (await recipients.ExistsAsync(command.UserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Success();
        }

        recipients.Add(recipient.Value);
        return Result.Success();
    }
}
