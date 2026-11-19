using Billing.Application.Abstractions;
using Billing.Domain.Customers;
using Billing.Domain.Invoices;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Microsoft.Extensions.Logging;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Billing.Application.Webhooks.SyncPaymentObject;

/// <summary>
/// Brings a subscription or invoice up to date with the provider. It reads the object as it is now
/// instead of trusting the event: events arrive late, twice and out of order, the object does not.
/// </summary>
public sealed record SyncPaymentObjectCommand(PaymentObjectKind Kind, string ObjectId) : ICommand;

internal sealed partial class SyncPaymentObjectHandler(
    IPaymentGateway gateway,
    IPlanRepository plans,
    ICustomerRepository customers,
    ISubscriptionRepository subscriptions,
    IInvoiceRepository invoices,
    TimeProvider timeProvider,
    ILogger<SyncPaymentObjectHandler> logger) : ICommandHandler<SyncPaymentObjectCommand>
{
    public async Task<Result> Handle(SyncPaymentObjectCommand command, CancellationToken cancellationToken)
    {
        if (command.Kind == PaymentObjectKind.Subscription)
        {
            var synced = await SyncSubscriptionAsync(command.ObjectId, cancellationToken).ConfigureAwait(false);
            return synced.IsFailure ? synced.Error : Result.Success();
        }

        var snapshot = await gateway.GetInvoiceAsync(command.ObjectId, cancellationToken).ConfigureAwait(false);
        if (snapshot.IsFailure)
        {
            return snapshot.Error;
        }

        // An invoice may arrive before its subscription: the subscription is read first.
        if (snapshot.Value.ProviderSubscriptionId is not { } subscriptionId)
        {
            return Result.Success();
        }

        var subscription = await SyncSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
        if (subscription.IsFailure)
        {
            return subscription.Error;
        }

        if (subscription.Value is { } owner)
        {
            await MirrorInvoiceAsync(snapshot.Value, owner, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }

    /// <summary>The mirrored subscription, or <see langword="null"/> for one that is not kept (a second live one).</summary>
    private async Task<Result<Subscription?>> SyncSubscriptionAsync(string providerSubscriptionId, CancellationToken cancellationToken)
    {
        var snapshot = await gateway.GetSubscriptionAsync(providerSubscriptionId, cancellationToken).ConfigureAwait(false);
        if (snapshot.IsFailure)
        {
            return snapshot.Error;
        }

        var current = snapshot.Value;
        var plan = current.ProviderPriceId is { } priceId
            ? await plans.GetByProviderPriceIdAsync(priceId, cancellationToken).ConfigureAwait(false)
            : null;
        if (plan is null)
        {
            LogUnknownPrice(logger, current.Id, current.ProviderPriceId);
            return PaymentErrors.UnknownPrice;
        }

        var state = new SubscriptionState(current.Status, plan.Id, current.CurrentPeriodEnd, current.CancelAtPeriodEnd);
        var now = timeProvider.GetUtcNow();

        var existing = await subscriptions.GetByProviderIdAsync(current.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.Apply(state, now);
            return existing;
        }

        var userId = current.UserId
            ?? (await customers.GetByProviderCustomerIdAsync(current.ProviderCustomerId, cancellationToken).ConfigureAwait(false))?.UserId;
        if (userId is not { } owner)
        {
            LogUnknownCustomer(logger, current.Id);
            return PaymentErrors.UnknownCustomer;
        }

        // Two checkouts paid side by side give two subscriptions at the provider; only one is kept.
        if (current.Status != SubscriptionStatus.Canceled && await subscriptions.HasLiveAsync(owner, cancellationToken).ConfigureAwait(false))
        {
            LogSecondLiveSubscription(logger, current.Id, owner);
            return (Subscription?)null;
        }

        var subscription = Subscription.Start(owner, current.Id, state, now);
        subscriptions.Add(subscription);
        return subscription;
    }

    private async Task MirrorInvoiceAsync(InvoiceSnapshot snapshot, Subscription subscription, CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetByProviderIdAsync(snapshot.Id, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            invoice = Invoice.Record(subscription.UserId, subscription.Id, snapshot.Id, snapshot.AmountMinor, snapshot.Currency, snapshot.IssuedAt);
            invoices.Add(invoice);
        }

        switch (snapshot.Status)
        {
            case ProviderInvoiceStatus.Paid:
                var card = CardSummary.Create(snapshot.CardBrand, snapshot.CardLast4);
                invoice.MarkPaid(snapshot.PaidAt ?? timeProvider.GetUtcNow(), card.IsSuccess ? card.Value : null);
                break;
            case ProviderInvoiceStatus.Void or ProviderInvoiceStatus.Uncollectible:
                invoice.MarkVoid();
                break;
            case ProviderInvoiceStatus.Open when snapshot.PaymentAttempted:
                invoice.MarkPaymentFailed();
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Subscription {ProviderSubscriptionId} is sold at price {ProviderPriceId}, which no plan has; the provider will send it again")]
    private static partial void LogUnknownPrice(ILogger logger, string providerSubscriptionId, string? providerPriceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Subscription {ProviderSubscriptionId} belongs to no known customer; the provider will send it again")]
    private static partial void LogUnknownCustomer(ILogger logger, string providerSubscriptionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Subscription {ProviderSubscriptionId} is a second live subscription of user {UserId} and is not kept; it needs to be canceled and refunded at the provider")]
    private static partial void LogSecondLiveSubscription(ILogger logger, string providerSubscriptionId, Guid userId);
}
