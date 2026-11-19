using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.API.Admin;
using Billing.Application.Plans;
using Billing.Application.Plans.GetPlan;
using Billing.Application.Plans.GetPlans;
using Billing.Application.Plans.GetPlansOnSale;
using Billing.Domain.Plans;
using Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Paging;

namespace Billing.IntegrationTests.Admin;

[Collection(nameof(BillingApiCollection))]
public sealed class PlanApiTests(BillingApiFixture api)
{
    private const string Read = "billing:plans:read";
    private const string Write = "billing:plans:write";

    private static readonly JsonSerializerOptions Json = CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("GET", "/admin/plans", Read)]
    [InlineData("GET", "/admin/plans/01a1bbcf-1a47-7189-a777-c599de32e2de", Read)]
    [InlineData("POST", "/admin/plans", Write)]
    [InlineData("PUT", "/admin/plans/01a1bbcf-1a47-7189-a777-c599de32e2de", Write)]
    [InlineData("POST", "/admin/plans/01a1bbcf-1a47-7189-a777-c599de32e2de/activate", Write)]
    [InlineData("POST", "/admin/plans/01a1bbcf-1a47-7189-a777-c599de32e2de/deactivate", Write)]
    public async Task Endpoints_NeedATokenAndTheirPermission(string method, string path, string permission)
    {
        using var anonymous = api.CreateAnonymousClient();
        using var unrelated = api.CreateClient(new[] { Read, Write, "billing:subscriptions:read" }.Where(item => item != permission).ToArray());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unrelated.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Plan_IsCreatedInactive_PutOnSale_AndTakenOff_WithEveryStepAudited()
    {
        using var admin = api.CreateClient(Read, Write);
        using var visitor = api.CreateAnonymousClient();
        var price = NewPrice();

        var created = await admin.PostAsJsonAsync("/admin/plans", Body(price, 9999), Json, Cancellation);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadAsync<CreatedResponse>(created)).Id;
        Assert.Equal($"/admin/plans/{id}", created.Headers.Location?.OriginalString);

        var details = await ReadAsync<PlanDetails>(await admin.GetAsync($"/admin/plans/{id}", Cancellation));
        Assert.False(details.IsActive);
        Assert.Equal(("Temel", 9999L, "TRY", price), (details.Name, details.AmountMinor, details.Currency, details.ProviderPriceId));
        Assert.Equal("Basic", details.Translations["en"].Name);
        Assert.DoesNotContain(await OnSaleAsync(visitor), plan => plan.Id == id);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/admin/plans/{id}/activate", null, Cancellation)).StatusCode);
        Assert.Contains(await OnSaleAsync(visitor), plan => plan.Id == id);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/admin/plans/{id}/deactivate", null, Cancellation)).StatusCode);
        Assert.DoesNotContain(await OnSaleAsync(visitor), plan => plan.Id == id);

        Assert.Equal(["CreatePlan", "ActivatePlan", "DeactivatePlan"], await AuditAsync(id));
    }

    [Fact]
    public async Task Update_NeedsTheCurrentVersion()
    {
        using var admin = api.CreateClient(Read, Write);
        var id = await CreateAsync(admin, NewPrice(), 9999);
        var version = (await ReadAsync<PlanDetails>(await admin.GetAsync($"/admin/plans/{id}", Cancellation))).Version;

        var updated = await admin.PutAsJsonAsync($"/admin/plans/{id}", Body(NewPrice(), 12999, version), Json, Cancellation);
        var stale = await admin.PutAsJsonAsync($"/admin/plans/{id}", Body(NewPrice(), 1, version), Json, Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("Plan.VersionConflict", await stale.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.Equal(12999, (await ReadAsync<PlanDetails>(await admin.GetAsync($"/admin/plans/{id}", Cancellation))).AmountMinor);
        Assert.Contains("UpdatePlan", await AuditAsync(id));
    }

    [Fact]
    public async Task AProviderPrice_BelongsToOnePlan()
    {
        using var admin = api.CreateClient(Read, Write);
        var price = NewPrice();
        var first = await CreateAsync(admin, price, 9999);
        var second = await CreateAsync(admin, NewPrice(), 9999);
        var version = (await ReadAsync<PlanDetails>(await admin.GetAsync($"/admin/plans/{second}", Cancellation))).Version;

        var created = await admin.PostAsJsonAsync("/admin/plans", Body(price, 9999), Json, Cancellation);
        var updated = await admin.PutAsJsonAsync($"/admin/plans/{second}", Body(price, 9999, version), Json, Cancellation);

        Assert.Equal(HttpStatusCode.Conflict, created.StatusCode);
        Assert.Contains("Plan.ProviderPriceTaken", await created.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, updated.StatusCode);
        Assert.Contains("Plan.ProviderPriceTaken", await updated.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task InvalidPlans_AreRefused()
    {
        using var admin = api.CreateClient(Read, Write);

        var free = await admin.PostAsJsonAsync("/admin/plans", Body(NewPrice(), 0), Json, Cancellation);
        var nameless = await admin.PostAsJsonAsync("/admin/plans", new { translations = new { en = new { name = "Basic" } }, amountMinor = 100, currency = "TRY", quality = "Hd", maxScreens = 1, providerPriceId = NewPrice() }, Json, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, free.StatusCode);
        Assert.Contains("Plan.AmountInvalid", await free.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, nameless.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/admin/plans/{Guid.NewGuid()}/activate", null, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task PlansOnSale_AreInTheAskedLanguage_FallBackToTurkish_AndHideTheProviderPrice()
    {
        using var admin = api.CreateClient(Read, Write);
        using var visitor = api.CreateAnonymousClient();
        var english = await CreateAsync(admin, NewPrice(), 1);
        var turkishOnly = await CreateAsync(admin, NewPrice(), 2, english: false);
        await admin.PostAsync($"/admin/plans/{english}/activate", null, Cancellation);
        await admin.PostAsync($"/admin/plans/{turkishOnly}/activate", null, Cancellation);

        var response = await visitor.GetAsync("/plans?lang=en", Cancellation);
        var plans = await ReadAsync<List<PlanOffer>>(response);
        var first = plans.Single(plan => plan.Id == english);
        var second = plans.Single(plan => plan.Id == turkishOnly);

        Assert.Equal(("Basic", "en"), (first.Name, first.Language));
        Assert.Equal(("Temel", "tr"), (second.Name, second.Language));
        Assert.True(plans.IndexOf(first) < plans.IndexOf(second));
        Assert.DoesNotContain("price_", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await visitor.GetAsync("/plans?lang=fr", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task AdminList_FiltersByActive_AndPages()
    {
        using var admin = api.CreateClient(Read, Write);
        var active = await CreateAsync(admin, NewPrice(), 5);
        await admin.PostAsync($"/admin/plans/{active}/activate", null, Cancellation);
        var inactive = await CreateAsync(admin, NewPrice(), 6);

        var onlyActive = await ReadAsync<Page<PlanSummary>>(await admin.GetAsync("/admin/plans?active=true&pageSize=100", Cancellation));
        var onlyInactive = await ReadAsync<Page<PlanSummary>>(await admin.GetAsync("/admin/plans?active=false&pageSize=100", Cancellation));

        Assert.Contains(onlyActive.Items, plan => plan.Id == active);
        Assert.DoesNotContain(onlyActive.Items, plan => plan.Id == inactive);
        Assert.Contains(onlyInactive.Items, plan => plan.Id == inactive);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/admin/plans?pageSize=101", Cancellation)).StatusCode);
    }

    private static string NewPrice() => $"price_{Guid.NewGuid():N}";

    private static object Body(string price, long amount, int version = 0, bool english = true)
    {
        var translations = new Dictionary<string, PlanTextFields> { ["tr"] = new("Temel", "SD izleme.") };
        if (english)
        {
            translations["en"] = new PlanTextFields("Basic", "Watch in SD.");
        }

        return new PlanRequest(translations, amount, "TRY", VideoQuality.Sd, 1, price, version);
    }

    private static async Task<Guid> CreateAsync(HttpClient admin, string price, long amount, bool english = true)
        => (await ReadAsync<CreatedResponse>(await admin.PostAsJsonAsync("/admin/plans", Body(price, amount, english: english), Json, Cancellation))).Id;

    private static async Task<List<PlanOffer>> OnSaleAsync(HttpClient client) => await ReadAsync<List<PlanOffer>>(await client.GetAsync("/plans", Cancellation));

    private async Task<List<string>> AuditAsync(Guid id)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();

        // A creation has no target yet; it is recognized by the plan it created, matched here and not in SQL (the details are jsonb).
        var plan = await context.Plans.AsNoTracking().SingleAsync(item => item.Id == id, Cancellation);
        var entries = await context.AuditLogEntries.AsNoTracking()
            .Where(entry => entry.TargetId == id || (entry.TargetId == null && entry.Action == "CreatePlan"))
            .ToListAsync(Cancellation);

        return entries
            .Where(entry => entry.TargetId == id || entry.Details.Contains(plan.ProviderPriceId, StringComparison.Ordinal))
            .OrderBy(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Id)
            .Select(entry => entry.Action)
            .ToList();
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancellation)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Cancellation))!;
    }
}
