using Billing.Application.Plans;
using Billing.Domain.Plans;

namespace Billing.API.Admin;

/// <summary>The permission strings this service checks; the same strings are in the auth service's catalog.</summary>
internal static class BillingPermissions
{
    public const string PlansRead = "billing:plans:read";
    public const string PlansWrite = "billing:plans:write";
    public const string SubscriptionsRead = "billing:subscriptions:read";
}

public sealed record CreatedResponse(Guid Id);

/// <param name="Version">On a change: the version that was loaded. Ignored when creating.</param>
public sealed record PlanRequest(
    IReadOnlyDictionary<string, PlanTextFields>? Translations,
    long AmountMinor,
    string? Currency,
    VideoQuality Quality,
    int MaxScreens,
    string? ProviderPriceId,
    int Version)
{
    internal PlanFields Fields => new(Translations ?? new Dictionary<string, PlanTextFields>(), AmountMinor, Currency, Quality, MaxScreens, ProviderPriceId);
}
