using System.Text.Json;
using Billing.Application.Webhooks;
using Microsoft.Extensions.Options;
using Shared.Domain;
using Stripe;

namespace Billing.Infrastructure.Payments;

/// <summary>
/// Verifies Stripe's signature over the exact body and keeps only what the event points at. The
/// event's API version is not checked: nothing is read from it but its id, type and object id.
/// </summary>
internal sealed class StripeWebhook(IOptions<StripeOptions> options, TimeProvider timeProvider) : IPaymentWebhook
{
    /// <summary>Older signatures are refused, so that a recorded call cannot be played again later.</summary>
    public const long ToleranceSeconds = 300;

    public Result<PaymentEvent?> Read(string payload, string? signature)
    {
        if (string.IsNullOrWhiteSpace(options.Value.WebhookSecret))
        {
            return WebhookErrors.Unavailable;
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            return WebhookErrors.SignatureInvalid;
        }

        try
        {
            EventUtility.ValidateSignature(payload, signature, options.Value.WebhookSecret, ToleranceSeconds, timeProvider.GetUtcNow().ToUnixTimeSeconds());
        }
        catch (StripeException)
        {
            return WebhookErrors.SignatureInvalid;
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var id = root.GetProperty("id").GetString()!;
        var type = root.GetProperty("type").GetString()!;
        var data = root.GetProperty("data").GetProperty("object");

        return type switch
        {
            "checkout.session.completed" => data.TryGetProperty("subscription", out var subscription) && subscription.ValueKind == JsonValueKind.String
                ? new PaymentEvent(id, type, PaymentObjectKind.Subscription, subscription.GetString()!)
                : (PaymentEvent?)null,
            "customer.subscription.created" or "customer.subscription.updated" or "customer.subscription.deleted"
                or "customer.subscription.paused" or "customer.subscription.resumed"
                => new PaymentEvent(id, type, PaymentObjectKind.Subscription, data.GetProperty("id").GetString()!),
            "invoice.finalized" or "invoice.paid" or "invoice.payment_failed" or "invoice.voided" or "invoice.marked_uncollectible"
                => new PaymentEvent(id, type, PaymentObjectKind.Invoice, data.GetProperty("id").GetString()!),
            _ => (PaymentEvent?)null,
        };
    }
}
