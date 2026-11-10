using System.Net;
using Billing.Application.Abstractions;
using Billing.Domain.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Domain;
using Stripe;

namespace Billing.Infrastructure.Payments;

/// <summary>
/// Stripe behind <see cref="IPaymentGateway"/>. Every call carries an idempotency key derived from
/// what it does, so a retry after a timeout never creates a second customer or session.
/// </summary>
internal sealed partial class StripePaymentGateway(IStripeClient client, IOptions<CheckoutOptions> checkout, ILogger<StripePaymentGateway> logger) : IPaymentGateway
{
    public const string UserIdMetadata = "userId";
    public const string PlanIdMetadata = "planId";

    public Task<Result<string>> EnsureCustomerAsync(Guid userId, string email, CancellationToken cancellationToken)
        => CallAsync("customer", async () =>
        {
            var customer = await new CustomerService(client).CreateAsync(
                new CustomerCreateOptions { Email = email, Metadata = new Dictionary<string, string> { [UserIdMetadata] = userId.ToString() } },
                new RequestOptions { IdempotencyKey = $"customer:{userId}" },
                cancellationToken).ConfigureAwait(false);
            return customer.Id;
        });

    public Task<Result<Uri>> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
        => CallAsync("checkout", async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var metadata = new Dictionary<string, string> { [UserIdMetadata] = request.UserId.ToString(), [PlanIdMetadata] = request.PlanId.ToString() };
            var session = await new Stripe.Checkout.SessionService(client).CreateAsync(
                new Stripe.Checkout.SessionCreateOptions
                {
                    Mode = "subscription",
                    Customer = request.ProviderCustomerId,
                    ClientReferenceId = request.UserId.ToString(),
                    LineItems = [new Stripe.Checkout.SessionLineItemOptions { Price = request.ProviderPriceId, Quantity = 1 }],
                    SuccessUrl = checkout.Value.SuccessUrl!.AbsoluteUri,
                    CancelUrl = checkout.Value.CancelUrl!.AbsoluteUri,
                    Metadata = metadata,
                    SubscriptionData = new Stripe.Checkout.SessionSubscriptionDataOptions { Metadata = metadata },
                },
                new RequestOptions { IdempotencyKey = $"checkout:{request.UserId}:{request.IdempotencyKey}" },
                cancellationToken).ConfigureAwait(false);
            return new Uri(session.Url);
        });

    public Task<Result<Uri>> CreatePortalAsync(string providerCustomerId, CancellationToken cancellationToken)
        => CallAsync("portal", async () =>
        {
            var session = await new Stripe.BillingPortal.SessionService(client).CreateAsync(
                new Stripe.BillingPortal.SessionCreateOptions { Customer = providerCustomerId, ReturnUrl = checkout.Value.PortalReturnUrl!.AbsoluteUri },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return new Uri(session.Url);
        });

    public Task<Result<SubscriptionSnapshot>> GetSubscriptionAsync(string providerSubscriptionId, CancellationToken cancellationToken)
        => CallAsync("subscription", async () =>
        {
            var subscription = await new SubscriptionService(client).GetAsync(providerSubscriptionId, cancellationToken: cancellationToken).ConfigureAwait(false);
            var item = subscription.Items?.Data?.FirstOrDefault();
            Guid? userId = subscription.Metadata is not null
                && subscription.Metadata.TryGetValue(UserIdMetadata, out var value)
                && Guid.TryParse(value, out var parsed) ? parsed : null;

            return new SubscriptionSnapshot(
                subscription.Id,
                subscription.CustomerId,
                StatusOf(subscription.Status),
                item?.Price?.Id,
                item is null ? null : Utc(item.CurrentPeriodEnd),
                subscription.CancelAtPeriodEnd,
                userId);
        });

    public Task<Result<InvoiceSnapshot>> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken)
        => CallAsync("invoice", async () =>
        {
            var invoice = await new InvoiceService(client).GetAsync(
                providerInvoiceId,
                new InvoiceGetOptions { Expand = ["payments"] },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            string? brand = null, last4 = null;
            var paymentIntentId = invoice.Payments?.Data?
                .Where(payment => payment.Status == "paid")
                .Select(payment => payment.Payment?.PaymentIntentId)
                .FirstOrDefault(id => id is not null);
            if (paymentIntentId is not null)
            {
                var intent = await new PaymentIntentService(client).GetAsync(
                    paymentIntentId,
                    new PaymentIntentGetOptions { Expand = ["payment_method"] },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                brand = intent.PaymentMethod?.Card?.Brand;
                last4 = intent.PaymentMethod?.Card?.Last4;
            }

            return new InvoiceSnapshot(
                invoice.Id,
                invoice.Parent?.SubscriptionDetails?.SubscriptionId,
                invoice.AmountDue,
                invoice.Currency.ToUpperInvariant(),
                InvoiceStatusOf(invoice.Status),
                invoice.Attempted,
                Utc(invoice.Created),
                invoice.StatusTransitions?.PaidAt is { } paidAt ? Utc(paidAt) : null,
                brand,
                last4);
        });

    // Stripe's statuses, to the four this service acts on: access follows Active alone.
    internal static SubscriptionStatus StatusOf(string status) => status switch
    {
        "active" or "trialing" => SubscriptionStatus.Active,
        "past_due" or "unpaid" or "paused" => SubscriptionStatus.PastDue,
        "canceled" or "incomplete_expired" => SubscriptionStatus.Canceled,
        _ => SubscriptionStatus.Incomplete,
    };

    internal static ProviderInvoiceStatus InvoiceStatusOf(string status) => status switch
    {
        "paid" => ProviderInvoiceStatus.Paid,
        "void" => ProviderInvoiceStatus.Void,
        "uncollectible" => ProviderInvoiceStatus.Uncollectible,
        "open" => ProviderInvoiceStatus.Open,
        _ => ProviderInvoiceStatus.Draft,
    };

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private async Task<Result<T>> CallAsync<T>(string operation, Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (StripeException exception) when (IsTransient(exception))
        {
            LogUnavailable(logger, exception, operation);
            return PaymentErrors.Unavailable;
        }
        catch (StripeException exception)
        {
            // Stripe's own error code and type only: its messages can echo request values.
            LogRejected(logger, operation, (int)exception.HttpStatusCode, exception.StripeError?.Type, exception.StripeError?.Code);
            return PaymentErrors.ProviderRejected;
        }
        catch (HttpRequestException exception)
        {
            LogUnavailable(logger, exception, operation);
            return PaymentErrors.Unavailable;
        }
    }

    // No answer, an answer from a struggling Stripe, or being told to slow down: worth trying again later.
    private static bool IsTransient(StripeException exception)
        => exception.HttpStatusCode is 0 or HttpStatusCode.TooManyRequests || (int)exception.HttpStatusCode >= 500;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stripe could not be reached for {Operation}")]
    private static partial void LogUnavailable(ILogger logger, Exception exception, string operation);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stripe refused {Operation} with status {StatusCode}, type {ErrorType}, code {ErrorCode}")]
    private static partial void LogRejected(ILogger logger, string operation, int statusCode, string? errorType, string? errorCode);
}

/// <summary>Stands in when no Stripe key is configured: payments are simply unavailable.</summary>
internal sealed class UnconfiguredPaymentGateway : IPaymentGateway
{
    public Task<Result<string>> EnsureCustomerAsync(Guid userId, string email, CancellationToken cancellationToken)
        => Task.FromResult<Result<string>>(PaymentErrors.Unavailable);

    public Task<Result<Uri>> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
        => Task.FromResult<Result<Uri>>(PaymentErrors.Unavailable);

    public Task<Result<Uri>> CreatePortalAsync(string providerCustomerId, CancellationToken cancellationToken)
        => Task.FromResult<Result<Uri>>(PaymentErrors.Unavailable);

    public Task<Result<SubscriptionSnapshot>> GetSubscriptionAsync(string providerSubscriptionId, CancellationToken cancellationToken)
        => Task.FromResult<Result<SubscriptionSnapshot>>(PaymentErrors.Unavailable);

    public Task<Result<InvoiceSnapshot>> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken)
        => Task.FromResult<Result<InvoiceSnapshot>>(PaymentErrors.Unavailable);
}
