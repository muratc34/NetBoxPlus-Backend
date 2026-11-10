using Billing.Domain.Subscriptions;
using Shared.Domain;

namespace Billing.Application.Abstractions;

/// <param name="IdempotencyKey">Sent to the provider, so that a repeated call does not open a second session.</param>
public sealed record CheckoutRequest(Guid UserId, Guid PlanId, string ProviderCustomerId, string ProviderPriceId, string IdempotencyKey);

/// <summary>A subscription as the provider holds it now.</summary>
/// <param name="UserId">From the metadata checkout put on it; absent on subscriptions made elsewhere.</param>
public sealed record SubscriptionSnapshot(
    string Id,
    string ProviderCustomerId,
    SubscriptionStatus Status,
    string? ProviderPriceId,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    Guid? UserId);

public enum ProviderInvoiceStatus
{
    Draft,
    Open,
    Paid,
    Void,
    Uncollectible,
}

/// <summary>An invoice as the provider holds it now; the card is only its brand and last four digits.</summary>
public sealed record InvoiceSnapshot(
    string Id,
    string? ProviderSubscriptionId,
    long AmountMinor,
    string Currency,
    ProviderInvoiceStatus Status,
    bool PaymentAttempted,
    DateTimeOffset IssuedAt,
    DateTimeOffset? PaidAt,
    string? CardBrand,
    string? CardLast4);

/// <summary>
/// The payment provider, behind which card details live; nothing of its own types reaches the
/// domain. Failures come back as <see cref="PaymentErrors"/>.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The provider's customer for the user, created at most once however often it is asked for.</summary>
    Task<Result<string>> EnsureCustomerAsync(Guid userId, string email, CancellationToken cancellationToken);

    /// <summary>A page at the provider where the customer subscribes to the plan.</summary>
    Task<Result<Uri>> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>A page at the provider where the customer manages card, plan and cancellation.</summary>
    Task<Result<Uri>> CreatePortalAsync(string providerCustomerId, CancellationToken cancellationToken);

    Task<Result<SubscriptionSnapshot>> GetSubscriptionAsync(string providerSubscriptionId, CancellationToken cancellationToken);

    Task<Result<InvoiceSnapshot>> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken);
}

public static class PaymentErrors
{
    public static readonly Error Unavailable = Error.Unavailable("Payment.Unavailable", "Payments are unavailable at the moment. Try again shortly.");

    public static readonly Error ProviderRejected = Error.UpstreamFailure("Payment.ProviderRejected", "The payment provider refused the request.");

    /// <summary>A failure, not a rejection: the plan may be added later, and the provider sends the event again.</summary>
    public static readonly Error UnknownPrice = Error.Failure("Payment.UnknownPrice", "No plan is sold at the subscription's provider price.");

    public static readonly Error UnknownCustomer = Error.Failure("Payment.UnknownCustomer", "The subscription belongs to no known customer.");
}
