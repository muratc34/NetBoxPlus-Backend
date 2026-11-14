namespace Billing.Contracts;

// Every subscription event carries the subscription's Revision, which goes up with each change of
// status or plan. Messages can arrive out of order: a consumer ignores one whose Revision is not
// higher than the last it applied for that subscription.
//
// Replayed is true when the billing service announces the current state again (for a consumer that
// started later), not a change: a consumer that only mirrors the state applies it as usual, one that
// tells the customer about changes ignores it. Fields added later are optional, so that messages
// written before them still read.

/// <summary>
/// The subscription now gives access: its first payment went through, it recovered from a failed
/// payment, or it moved to another plan while active. Carries what the plan allows.
/// </summary>
/// <param name="VideoQuality"><c>Sd</c>, <c>Hd</c> or <c>Uhd</c>.</param>
/// <param name="Reason"><c>Started</c>, <c>Resumed</c> or <c>PlanChanged</c>; null when replayed.</param>
public sealed record SubscriptionActivated(
    Guid SubscriptionId,
    Guid UserId,
    Guid PlanId,
    string VideoQuality,
    int MaxScreens,
    DateTimeOffset? CurrentPeriodEnd,
    int Revision,
    DateTimeOffset OccurredAt,
    string? Reason = null,
    bool Replayed = false);

/// <summary>A payment failed and the subscription gives no access until it is paid; the provider keeps trying.</summary>
public sealed record SubscriptionSuspended(Guid SubscriptionId, Guid UserId, int Revision, DateTimeOffset OccurredAt, bool Replayed = false);

/// <summary>The subscription ended for good.</summary>
public sealed record SubscriptionCanceled(Guid SubscriptionId, Guid UserId, DateTimeOffset CanceledAt, int Revision, DateTimeOffset OccurredAt, bool Replayed = false);

/// <summary>A payment of an invoice failed for the first time; for telling the customer.</summary>
public sealed record PaymentFailed(Guid InvoiceId, Guid SubscriptionId, Guid UserId, long AmountMinor, string Currency, DateTimeOffset OccurredAt);
