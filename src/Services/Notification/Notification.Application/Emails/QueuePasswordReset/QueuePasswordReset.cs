using Auth.Contracts;
using Notification.Application.Abstractions;
using Notification.Domain.Emails;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.Application.Emails.QueuePasswordReset;

/// <summary>
/// Queues the e-mail with a password reset link: one per token, so a second delivery of the same
/// token changes nothing. A token that has already expired is not e-mailed; the user asks again.
/// </summary>
public sealed record QueuePasswordResetCommand(Guid UserId, Guid TokenId, string Token, DateTimeOffset ExpiresAt) : ICommand;

internal sealed class QueuePasswordResetHandler(IOutgoingEmailRepository emails, IEmailLinks links, TimeProvider timeProvider)
    : ICommandHandler<QueuePasswordResetCommand>
{
    public async Task<Result> Handle(QueuePasswordResetCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (command.ExpiresAt <= now || string.IsNullOrWhiteSpace(command.Token))
        {
            return Result.Success();
        }

        var sourceKey = command.TokenId.ToString("N");
        if (await emails.ExistsAsync(EmailKind.PasswordReset, sourceKey, cancellationToken).ConfigureAwait(false))
        {
            return Result.Success();
        }

        var link = links.To("reset-password?token=" + Uri.EscapeDataString(command.Token));
        var content = EmailTemplates.PasswordReset(link, command.ExpiresAt - now);
        var email = OutgoingEmail.Queue(command.UserId, EmailKind.PasswordReset, sourceKey, content, now);
        if (email.IsFailure)
        {
            return email.Error;
        }

        emails.Add(email.Value);
        return Result.Success();
    }
}

/// <summary>
/// The reset e-mail for every token the account service issues. The full type name is this
/// handler's inbox key: renaming or moving it would process the messages it already handled again.
/// </summary>
internal sealed class QueuePasswordResetOnRequested(IDispatcher dispatcher) : IIntegrationEventHandler<PasswordResetRequested>
{
    public Task<Result> Handle(PasswordResetRequested integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(
            new QueuePasswordResetCommand(integrationEvent.UserId, integrationEvent.TokenId, integrationEvent.Token, integrationEvent.ExpiresAt),
            cancellationToken);
}
