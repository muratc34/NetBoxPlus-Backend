namespace Billing.Infrastructure.Payments;

/// <summary>The <c>Billing:Stripe</c> section. Without a key the service starts, but payments answer 503.</summary>
public sealed class StripeOptions
{
    public const string SectionName = "Billing:Stripe";

    /// <summary>A secret: from user secrets or the environment, never from a committed file.</summary>
    public string? SecretKey { get; set; }

    /// <summary>The webhook's signing secret (<c>whsec_...</c>). Without it the webhook endpoint answers 503.</summary>
    public string? WebhookSecret { get; set; }
}

/// <summary>
/// The <c>Billing:Checkout</c> section: where the provider sends customers back. Fixed here, never
/// taken from a request, so that nobody can be sent to someone else's site.
/// </summary>
public sealed class CheckoutOptions
{
    public const string SectionName = "Billing:Checkout";

    public Uri? SuccessUrl { get; set; }

    public Uri? CancelUrl { get; set; }

    public Uri? PortalReturnUrl { get; set; }

    internal bool IsValid => SuccessUrl is { IsAbsoluteUri: true } && CancelUrl is { IsAbsoluteUri: true } && PortalReturnUrl is { IsAbsoluteUri: true };
}
