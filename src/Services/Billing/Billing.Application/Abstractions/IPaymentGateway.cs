using Shared.Domain;

namespace Billing.Application.Abstractions;

/// <param name="IdempotencyKey">Sent to the provider, so that a repeated call does not open a second session.</param>
public sealed record CheckoutRequest(Guid UserId, Guid PlanId, string ProviderCustomerId, string ProviderPriceId, string IdempotencyKey);

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
}

public static class PaymentErrors
{
    public static readonly Error Unavailable = Error.Unavailable("Payment.Unavailable", "Payments are unavailable at the moment. Try again shortly.");

    public static readonly Error ProviderRejected = Error.UpstreamFailure("Payment.ProviderRejected", "The payment provider refused the request.");
}
