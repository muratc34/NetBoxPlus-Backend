using Shared.Domain;

namespace Billing.Application.Webhooks;

public enum PaymentObjectKind
{
    Subscription,
    Invoice,
}

/// <summary>A provider event, reduced to what it is about: the object to look at again.</summary>
public sealed record PaymentEvent(string Id, string Type, PaymentObjectKind Kind, string ObjectId);

/// <summary>Reads and verifies the provider's webhook calls.</summary>
public interface IPaymentWebhook
{
    /// <summary>
    /// The event, <see langword="null"/> for an event this service does not follow, or
    /// <see cref="WebhookErrors.SignatureInvalid"/> / <see cref="WebhookErrors.Unavailable"/>.
    /// </summary>
    Result<PaymentEvent?> Read(string payload, string? signature);
}

public static class WebhookErrors
{
    public static readonly Error SignatureInvalid = Error.Validation("Webhook.SignatureInvalid", "The webhook signature is missing, invalid or too old.");

    public static readonly Error Unavailable = Error.Unavailable("Webhook.Unavailable", "Webhooks are not configured.");
}
