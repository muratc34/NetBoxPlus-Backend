using Auth.Contracts;
using Notification.Application.Abstractions;
using Notification.Domain.Emails;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.Application.Emails.QueueEmailChange;

/// <summary>
/// Queues the link that moves the account to <paramref name="NewEmail"/>, sent to that address rather
/// than the user's current one. One per token, so a second delivery changes nothing; a token that has
/// already expired is not e-mailed.
/// </summary>
public sealed record QueueEmailChangeCommand(Guid UserId, Guid TokenId, string Token, string? NewEmail, DateTimeOffset ExpiresAt) : ICommand;

internal sealed class QueueEmailChangeHandler(IOutgoingEmailRepository emails, IEmailLinks links, TimeProvider timeProvider)
    : ICommandHandler<QueueEmailChangeCommand>
{
    public async Task<Result> Handle(QueueEmailChangeCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (command.ExpiresAt <= now || string.IsNullOrWhiteSpace(command.Token))
        {
            return Result.Success();
        }

        var sourceKey = command.TokenId.ToString("N");
        if (await emails.ExistsAsync(EmailKind.EmailChangeVerification, sourceKey, cancellationToken).ConfigureAwait(false))
        {
            return Result.Success();
        }

        var link = links.To("change-email?token=" + Uri.EscapeDataString(command.Token));
        var content = EmailTemplates.EmailChangeVerification(link, command.ExpiresAt - now);
        var email = OutgoingEmail.Queue(command.UserId, EmailKind.EmailChangeVerification, sourceKey, content, now, command.NewEmail ?? string.Empty);
        if (email.IsFailure)
        {
            return email.Error;
        }

        emails.Add(email.Value);
        return Result.Success();
    }
}

/// <summary>
/// The change link for every token the account service issues. The full type name is this handler's
/// inbox key: renaming or moving it would process the messages it already handled again.
/// </summary>
internal sealed class QueueEmailChangeOnRequested(IDispatcher dispatcher) : IIntegrationEventHandler<EmailChangeRequested>
{
    public Task<Result> Handle(EmailChangeRequested integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(
            new QueueEmailChangeCommand(integrationEvent.UserId, integrationEvent.TokenId, integrationEvent.Token, integrationEvent.NewEmail, integrationEvent.ExpiresAt),
            cancellationToken);
}
