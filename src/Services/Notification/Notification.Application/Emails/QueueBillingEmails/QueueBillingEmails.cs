using Billing.Contracts;
using Notification.Application.Abstractions;
using Notification.Domain.Emails;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.Application.Emails.QueueBillingEmails;

/// <summary>
/// Queues an e-mail about a billing event, once per <paramref name="SourceKey"/>: a second delivery
/// of the same event changes nothing.
/// </summary>
public sealed record QueueBillingEmailCommand(Guid UserId, EmailKind Kind, string SourceKey, EmailContent Content) : ICommand;

internal sealed class QueueBillingEmailHandler(IOutgoingEmailRepository emails, TimeProvider timeProvider) : ICommandHandler<QueueBillingEmailCommand>
{
    public async Task<Result> Handle(QueueBillingEmailCommand command, CancellationToken cancellationToken)
    {
        if (await emails.ExistsAsync(command.Kind, command.SourceKey, cancellationToken).ConfigureAwait(false))
        {
            return Result.Success();
        }

        var email = OutgoingEmail.Queue(command.UserId, command.Kind, command.SourceKey, command.Content, timeProvider.GetUtcNow());
        if (email.IsFailure)
        {
            return email.Error;
        }

        emails.Add(email.Value);
        return Result.Success();
    }
}

/// <summary>
/// The customer's e-mails about the subscription, from the billing service: started, resumed after a
/// failed payment, payment failed, ended. Each tells of its own event, so they are keyed by the event
/// (subscription and revision, or invoice) and not put in order: a late one still tells something
/// that happened. Replayed state is no event and sends nothing; a plan change and a suspension send
/// nothing either (the customer made the change; the failed payment has its own e-mail). The full
/// type name is this handler's inbox key: renaming or moving it would process its messages again.
/// </summary>
internal sealed class QueueBillingEmails(IDispatcher dispatcher, IEmailLinks links)
    : IIntegrationEventHandler<SubscriptionActivated>, IIntegrationEventHandler<PaymentFailed>, IIntegrationEventHandler<SubscriptionCanceled>
{
    public Task<Result> Handle(SubscriptionActivated integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Replayed)
        {
            return Task.FromResult(Result.Success());
        }

        var sourceKey = SubscriptionKey(integrationEvent.SubscriptionId, integrationEvent.Revision);
        return integrationEvent.Reason switch
        {
            "Started" => QueueAsync(
                integrationEvent.UserId,
                EmailKind.SubscriptionStarted,
                sourceKey,
                EmailTemplates.SubscriptionStarted(integrationEvent.VideoQuality, integrationEvent.MaxScreens, integrationEvent.CurrentPeriodEnd, links.To(string.Empty)),
                cancellationToken),
            "Resumed" => QueueAsync(
                integrationEvent.UserId,
                EmailKind.SubscriptionResumed,
                sourceKey,
                EmailTemplates.SubscriptionResumed(links.To(string.Empty)),
                cancellationToken),
            _ => Task.FromResult(Result.Success()),
        };
    }

    public Task<Result> Handle(PaymentFailed integrationEvent, CancellationToken cancellationToken)
        => QueueAsync(
            integrationEvent.UserId,
            EmailKind.PaymentFailed,
            integrationEvent.InvoiceId.ToString("N"),
            EmailTemplates.PaymentFailed(integrationEvent.AmountMinor, integrationEvent.Currency, links.To("account/subscription")),
            cancellationToken);

    public Task<Result> Handle(SubscriptionCanceled integrationEvent, CancellationToken cancellationToken)
        => integrationEvent.Replayed
            ? Task.FromResult(Result.Success())
            : QueueAsync(
                integrationEvent.UserId,
                EmailKind.SubscriptionEnded,
                SubscriptionKey(integrationEvent.SubscriptionId, integrationEvent.Revision),
                EmailTemplates.SubscriptionEnded(integrationEvent.CanceledAt, links.To("plans")),
                cancellationToken);

    private static string SubscriptionKey(Guid subscriptionId, int revision)
        => $"{subscriptionId:N}-{revision.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private Task<Result> QueueAsync(Guid userId, EmailKind kind, string sourceKey, EmailContent content, CancellationToken cancellationToken)
        => dispatcher.Send(new QueueBillingEmailCommand(userId, kind, sourceKey, content), cancellationToken);
}
