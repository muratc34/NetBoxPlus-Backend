using Billing.Domain.Invoices;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;

namespace Billing.Application.Subscriptions;

/// <param name="Language">The language the name is actually in.</param>
public sealed record PlanReference(Guid Id, string Name, string Language, VideoQuality Quality, int MaxScreens);

/// <summary>An invoice as its customer sees it: the card only as brand and last four digits.</summary>
public sealed record InvoiceSummary(
    Guid Id,
    long AmountMinor,
    string Currency,
    InvoiceStatus Status,
    DateTimeOffset IssuedAt,
    DateTimeOffset? PaidAt,
    string? CardBrand,
    string? CardLast4);

internal static class SubscriptionViews
{
    public static PlanReference ToReference(this Plan plan, string language)
    {
        var text = plan.TextFor(language);
        return new PlanReference(plan.Id, text.Name, text.Language, plan.Quality, plan.MaxScreens);
    }

    public static InvoiceSummary ToSummary(this Invoice invoice)
        => new(invoice.Id, invoice.AmountMinor, invoice.Currency, invoice.Status, invoice.IssuedAt, invoice.PaidAt, invoice.CardBrand, invoice.CardLast4);

    /// <summary>A live subscription first; without one, the latest that ended.</summary>
    public static IQueryable<Subscription> CurrentFirst(this IQueryable<Subscription> subscriptions)
        => subscriptions
            .OrderBy(subscription => subscription.Status == SubscriptionStatus.Canceled)
            .ThenByDescending(subscription => subscription.CreatedAt)
            .ThenByDescending(subscription => subscription.Id);
}
