using Microsoft.Extensions.Logging;
using NetBoxPlus.DemoSeeder.Gateway;
using Stripe;

namespace NetBoxPlus.DemoSeeder.Seeding;

internal sealed record PlanSeed(string LookupKey, string NameTurkish, string NameEnglish, string DescriptionTurkish, string DescriptionEnglish, long AmountMinor, string Quality, int MaxScreens);

/// <summary>
/// The subscription plans: a monthly Stripe price for each (test mode, found again by its lookup key),
/// then the billing plan that sells it, activated.
/// </summary>
internal sealed class PlanSeeder(GatewayClient gateway, SeederOptions options, ILogger<PlanSeeder> logger)
{
    private const string Billing = "/admin/billing";

    public static readonly IReadOnlyList<PlanSeed> Plans =
    [
        new("netboxplus-demo-basic", "Temel", "Basic", "Tek ekranda SD kalite.", "SD quality on one screen.", 14999, "Sd", 1),
        new("netboxplus-demo-premium", "Premium", "Premium", "Dört ekranda HD kalite.", "HD quality on four screens.", 24999, "Hd", 4),
    ];

    public int Changes { get; private set; }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.StripeSecretKey))
        {
            SeederLog.PlansSkipped(logger);
            return;
        }

        if (!SeedDecisions.IsTestModeKey(options.StripeSecretKey))
        {
            throw new InvalidOperationException("The Stripe key is not a test-mode key; the seeder only creates prices in test mode.");
        }

        var prices = new PriceService(new StripeClient(options.StripeSecretKey));
        var existing = await ExistingPlansAsync(cancellationToken).ConfigureAwait(false);
        foreach (var plan in Plans)
        {
            var priceId = await PriceAsync(prices, plan, cancellationToken).ConfigureAwait(false);
            SeederLog.PlanPrice(logger, plan.NameEnglish, priceId);

            if (!existing.TryGetValue(priceId, out var stored))
            {
                var created = await gateway.PostAsync<Created>(
                    $"{Billing}/plans",
                    new
                    {
                        translations = new Dictionary<string, object>
                        {
                            ["tr"] = new { name = plan.NameTurkish, description = plan.DescriptionTurkish },
                            ["en"] = new { name = plan.NameEnglish, description = plan.DescriptionEnglish },
                        },
                        amountMinor = plan.AmountMinor,
                        currency = "TRY",
                        quality = plan.Quality,
                        maxScreens = plan.MaxScreens,
                        providerPriceId = priceId,
                        version = 0,
                    },
                    cancellationToken).ConfigureAwait(false);
                stored = new PlanDetails(created.Id, priceId, IsActive: false);
                Changes++;
                SeederLog.Created(logger, "Plan", plan.NameEnglish);
            }

            if (!stored.IsActive)
            {
                await gateway.PostAsync($"{Billing}/plans/{stored.Id}/activate", body: null, cancellationToken).ConfigureAwait(false);
                Changes++;
                SeederLog.Step(logger, "Plan", plan.NameEnglish, "activated");
            }
        }
    }

    private static async Task<string> PriceAsync(PriceService prices, PlanSeed plan, CancellationToken cancellationToken)
    {
        var found = await prices.ListAsync(new PriceListOptions { LookupKeys = [plan.LookupKey], Active = true, Limit = 1 }, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (found.Data.FirstOrDefault() is { } price)
        {
            return price.Id;
        }

        var created = await prices.CreateAsync(
            new PriceCreateOptions
            {
                Currency = "try",
                UnitAmount = plan.AmountMinor,
                Recurring = new PriceRecurringOptions { Interval = "month" },
                LookupKey = plan.LookupKey,
                ProductData = new PriceProductDataOptions { Name = $"NetBoxPlus {plan.NameEnglish}" },
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return created.Id;
    }

    /// <summary>The plans billing has, by the Stripe price they sell.</summary>
    private async Task<Dictionary<string, PlanDetails>> ExistingPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new Dictionary<string, PlanDetails>(StringComparer.Ordinal);
        for (var page = 1; ; page++)
        {
            var items = await gateway.GetAsync<Page<PlanItem>>($"{Billing}/plans?page={page}&pageSize=100", cancellationToken).ConfigureAwait(false);
            foreach (var item in items.Items)
            {
                var details = await gateway.GetAsync<PlanDetails>($"{Billing}/plans/{item.Id}", cancellationToken).ConfigureAwait(false);
                plans[details.ProviderPriceId] = details;
            }

            if (page * items.PageSize >= items.TotalCount || items.Items.Count == 0)
            {
                return plans;
            }
        }
    }
}
