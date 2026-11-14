using Auth.Contracts;
using Notification.Application.Abstractions;
using Notification.Domain.Emails;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.Application.Emails.QueueEmailVerification;

/// <summary>
/// Queues the e-mail with a verification link: one per token, so a second delivery of the same token
/// changes nothing. A token that has already expired is not e-mailed; the user asks for a new one.
/// </summary>
public sealed record QueueEmailVerificationCommand(Guid UserId, Guid TokenId, string Token, DateTimeOffset ExpiresAt) : ICommand;

internal sealed class QueueEmailVerificationHandler(IOutgoingEmailRepository emails, IEmailLinks links, TimeProvider timeProvider)
    : ICommandHandler<QueueEmailVerificationCommand>
{
    public async Task<Result> Handle(QueueEmailVerificationCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (command.ExpiresAt <= now || string.IsNullOrWhiteSpace(command.Token))
        {
            return Result.Success();
        }

        var sourceKey = command.TokenId.ToString("N");
        if (await emails.ExistsAsync(EmailKind.EmailVerification, sourceKey, cancellationToken).ConfigureAwait(false))
        {
            return Result.Success();
        }

        var link = links.To("verify-email?token=" + Uri.EscapeDataString(command.Token));
        var content = EmailTemplates.EmailVerification(link, command.ExpiresAt - now);
        var email = OutgoingEmail.Queue(command.UserId, EmailKind.EmailVerification, sourceKey, content, now);
        if (email.IsFailure)
        {
            return email.Error;
        }

        emails.Add(email.Value);
        return Result.Success();
    }
}

/// <summary>
/// The verification e-mail for every token the account service issues. The full type name is this
/// handler's inbox key: renaming or moving it would process the messages it already handled again.
/// </summary>
internal sealed class QueueEmailVerificationOnRequested(IDispatcher dispatcher) : IIntegrationEventHandler<EmailVerificationRequested>
{
    public Task<Result> Handle(EmailVerificationRequested integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(
            new QueueEmailVerificationCommand(integrationEvent.UserId, integrationEvent.TokenId, integrationEvent.Token, integrationEvent.ExpiresAt),
            cancellationToken);
}
