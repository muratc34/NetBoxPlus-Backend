using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.API.Admin;
using Billing.Application.Plans;
using Billing.Application.Subscriptions;
using Billing.Application.Subscriptions.GetMySubscription;
using Billing.Application.Subscriptions.GetSubscription;
using Billing.Application.Subscriptions.GetSubscriptions;
using Billing.Domain.Invoices;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Billing.Infrastructure.Persistence;
using Billing.IntegrationTests.Admin;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Paging;

namespace Billing.IntegrationTests.Account;

[Collection(nameof(BillingApiCollection))]
public sealed class AccountApiTests(BillingApiFixture api)
{
    private const string Read = "billing:subscriptions:read";
    private static readonly JsonSerializerOptions Json = CreateJson();
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutASubscription_ThereIsNothingToShow()
    {
        using var client = api.CreateClientFor(Guid.CreateVersion7());
        using var anonymous = api.CreateAnonymousClient();

        var response = await client.GetAsync("/subscription", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Subscription.None", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.Empty((await ReadAsync<Page<InvoiceSummary>>(await client.GetAsync("/invoices", Cancellation))).Items);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/subscription", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/invoices", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task TheLiveSubscription_IsShownBeforeEndedOnes_InTheAskedLanguage()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync();
        await SubscriptionAsync(user, plan, SubscriptionStatus.Canceled);
        var live = await SubscriptionAsync(user, plan, SubscriptionStatus.Active);
        using var client = api.CreateClientFor(user);

        var mine = await ReadAsync<MySubscription>(await client.GetAsync("/subscription?lang=en", Cancellation));

        Assert.Equal((live.Id, SubscriptionStatus.Active), (mine.Id, mine.Status));
        Assert.Equal(("Basic", "en", VideoQuality.Hd, 2), (mine.Plan.Name, mine.Plan.Language, mine.Plan.Quality, mine.Plan.MaxScreens));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/subscription?lang=fr", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task WhenAllEnded_TheLatestIsShown()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync();
        await SubscriptionAsync(user, plan, SubscriptionStatus.Canceled);
        var latest = await SubscriptionAsync(user, plan, SubscriptionStatus.Canceled);
        using var client = api.CreateClientFor(user);

        var mine = await ReadAsync<MySubscription>(await client.GetAsync("/subscription", Cancellation));

        Assert.Equal((latest.Id, SubscriptionStatus.Canceled), (mine.Id, mine.Status));
        Assert.NotNull(mine.CanceledAt);
    }

    [Fact]
    public async Task Invoices_AreTheCallersOwn_NewestFirst_AndPaged()
    {
        var user = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var plan = await PlanAsync();
        var subscription = await SubscriptionAsync(user, plan, SubscriptionStatus.Active);
        var otherSubscription = await SubscriptionAsync(other, plan, SubscriptionStatus.Active);
        await InvoiceAsync(subscription, Now.AddMonths(-2), paid: true);
        await InvoiceAsync(subscription, Now.AddMonths(-1), paid: true);
        var newest = await InvoiceAsync(subscription, Now, paid: false);
        await InvoiceAsync(otherSubscription, Now, paid: true);
        using var client = api.CreateClientFor(user);

        var page = await ReadAsync<Page<InvoiceSummary>>(await client.GetAsync("/invoices?pageSize=2", Cancellation));
        var rest = await ReadAsync<Page<InvoiceSummary>>(await client.GetAsync("/invoices?pageSize=2&page=2", Cancellation));

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(newest.Id, page.Items[0].Id);
        Assert.Equal(InvoiceStatus.PaymentFailed, page.Items[0].Status);
        Assert.Equal(("visa", "4242"), (page.Items[1].CardBrand, page.Items[1].CardLast4));
        Assert.Single(rest.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/invoices?pageSize=101", Cancellation)).StatusCode);
    }

    [Theory]
    [InlineData("/admin/subscriptions")]
    [InlineData("/admin/subscriptions/01a1bbcf-1a47-7189-a777-c599de32e2de")]
    public async Task AdminReads_NeedTheirPermission(string path)
    {
        using var anonymous = api.CreateAnonymousClient();
        using var unrelated = api.CreateClient("billing:plans:read", "billing:plans:write");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unrelated.GetAsync(path, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Admin_ListsAndFiltersSubscriptions_AndSeesOneWithItsInvoices()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync();
        await CustomerAsync(user);
        var ended = await SubscriptionAsync(user, plan, SubscriptionStatus.Canceled);
        var live = await SubscriptionAsync(user, plan, SubscriptionStatus.PastDue);
        var invoice = await InvoiceAsync(live, Now, paid: false);
        using var admin = api.CreateClient(Read);

        var all = await ReadAsync<Page<SubscriptionSummary>>(await admin.GetAsync($"/admin/subscriptions?userId={user}", Cancellation));
        var pastDue = await ReadAsync<Page<SubscriptionSummary>>(await admin.GetAsync($"/admin/subscriptions?userId={user}&status=PastDue", Cancellation));
        var details = await ReadAsync<SubscriptionDetails>(await admin.GetAsync($"/admin/subscriptions/{live.Id}", Cancellation));

        Assert.Equal([live.Id, ended.Id], all.Items.Select(item => item.Id));
        Assert.Equal(($"{user:N}@example.com", "Temel"), (all.Items[0].Email, all.Items[0].PlanName));
        Assert.Equal(live.Id, Assert.Single(pastDue.Items).Id);
        Assert.Equal((live.ProviderSubscriptionId, 1, invoice.Id), (details.ProviderSubscriptionId, details.Revision, Assert.Single(details.Invoices).Id));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/admin/subscriptions/{Guid.NewGuid()}", Cancellation)).StatusCode);
    }

    private async Task<(Guid Id, string PriceId)> PlanAsync()
    {
        using var admin = api.CreateClient("billing:plans:read", "billing:plans:write");
        var price = $"price_{Guid.NewGuid():N}";
        var body = new PlanRequest(
            new Dictionary<string, PlanTextFields> { ["tr"] = new("Temel", null), ["en"] = new("Basic", null) },
            9999, "TRY", VideoQuality.Hd, 2, price, 0);
        return ((await ReadAsync<CreatedResponse>(await admin.PostAsJsonAsync("/admin/plans", body, Json, Cancellation))).Id, price);
    }

    private async Task CustomerAsync(Guid user)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        context.Customers.Add(Billing.Domain.Customers.BillingCustomer.Create(user, $"{user:N}@example.com"));
        await context.SaveChangesAsync(Cancellation);
    }

    private async Task<Subscription> SubscriptionAsync(Guid user, (Guid Id, string PriceId) plan, SubscriptionStatus status)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var subscription = Subscription.Start(user, $"sub_{Guid.NewGuid():N}", new SubscriptionState(status, plan.Id, Now.AddMonths(1), false), Now);
        context.Subscriptions.Add(subscription);
        await context.SaveChangesAsync(Cancellation);
        return subscription;
    }

    private async Task<Invoice> InvoiceAsync(Subscription subscription, DateTimeOffset issuedAt, bool paid)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var invoice = Invoice.Record(subscription.UserId, subscription.Id, $"in_{Guid.NewGuid():N}", 9999, "TRY", issuedAt);
        if (paid)
        {
            invoice.MarkPaid(issuedAt, CardSummary.Create("visa", "4242").Value);
        }
        else
        {
            invoice.MarkPaymentFailed();
        }

        context.Invoices.Add(invoice);
        await context.SaveChangesAsync(Cancellation);
        return invoice;
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
