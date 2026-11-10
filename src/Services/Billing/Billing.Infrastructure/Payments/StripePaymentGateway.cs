using System.Net;
using Billing.Application.Abstractions;
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
}
