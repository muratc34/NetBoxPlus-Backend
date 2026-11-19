using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.API.Admin;
using Billing.Application.Abstractions;
using Billing.Application.Plans;
using Billing.Domain.Customers;
using Billing.Domain.Invoices;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Billing.Infrastructure.Payments;
using Billing.Infrastructure.Persistence;
using Billing.IntegrationTests.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stripe;

namespace Billing.IntegrationTests.Webhooks;

/// <summary>Signed with the fixture's secret through Stripe's own scheme; the API reads objects back from the fake provider.</summary>
[Collection(nameof(BillingApiCollection))]
public sealed class WebhookApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = CreateJson();
    private static readonly DateTimeOffset PeriodEnd = new(2026, 12, 10, 0, 0, 0, TimeSpan.Zero);

    private readonly BillingApiFixture _api;

    public WebhookApiTests(BillingApiFixture api)
    {
        _api = api;
        _api.Payments.Reset();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _api.Payments.Reset();

    [Fact]
    public async Task UnsignedOrForgedOrOldCalls_AreRefused()
    {
        using var stripe = _api.CreateAnonymousClient();
        var payload = Event("customer.subscription.updated", "sub_x");

        var unsigned = await stripe.PostAsync("/webhooks/stripe", new StringContent(payload, Encoding.UTF8, "application/json"), Cancellation);
        var forged = await SendAsync(stripe, payload, EventUtility.GenerateSignatureHeader(payload, "whsec_someone_else"));
        var tampered = await SendAsync(stripe, payload.Replace("sub_x", "sub_y", StringComparison.Ordinal), EventUtility.GenerateSignatureHeader(payload, BillingApiFixture.WebhookSecret));
        var old = await SendAsync(stripe, payload, EventUtility.GenerateSignatureHeader(payload, BillingApiFixture.WebhookSecret, DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds()));

        foreach (var response in new[] { unsigned, forged, tampered, old })
        {
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "Webhook.SignatureInvalid");
        }
    }

    [Fact]
    public async Task ASubscriptionEvent_MirrorsTheSubscriptionAsTheProviderHasItNow()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync();
        var id = NewId("sub");
        _api.Payments.Subscriptions[id] = Subscription(id, plan.PriceId, SubscriptionStatus.Active, user);

        Assert.Equal(HttpStatusCode.OK, (await DeliverAsync(Event("customer.subscription.created", id))).StatusCode);

        var stored = await SubscriptionAsync(id);
        Assert.Equal((user, plan.Id, SubscriptionStatus.Active, (DateTimeOffset?)PeriodEnd), (stored.UserId, stored.PlanId, stored.Status, stored.CurrentPeriodEnd));
    }

    [Fact]
    public async Task TheSameEventTwice_IsHandledOnce()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync();
        var id = NewId("sub");
        _api.Payments.Subscriptions[id] = Subscription(id, plan.PriceId, SubscriptionStatus.Active, user);
        var payload = Event("customer.subscription.created", id);
        await DeliverAsync(payload);

        _api.Payments.Subscriptions[id] = Subscription(id, plan.PriceId, SubscriptionStatus.PastDue, user);
        Assert.Equal(HttpStatusCode.OK, (await DeliverAsync(payload)).StatusCode);
        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync(id)).Status);

        await DeliverAsync(Event("customer.subscription.updated", id));
        Assert.Equal(SubscriptionStatus.PastDue, (await SubscriptionAsync(id)).Status);
    }

    [Fact]
    public async Task ALateEvent_CannotReviveACanceledSubscription()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync();
        var id = NewId("sub");
        _api.Payments.Subscriptions[id] = Subscription(id, plan.PriceId, SubscriptionStatus.Canceled, user);
        await DeliverAsync(Event("customer.subscription.deleted", id));

        _api.Payments.Subscriptions[id] = Subscription(id, plan.PriceId, SubscriptionStatus.Active, user);
        await DeliverAsync(Event("customer.subscription.updated", id));

        Assert.Equal(SubscriptionStatus.Canceled, (await SubscriptionAsync(id)).Status);
    }

    [Fact]
    public async Task APaidInvoice_ArrivingBeforeItsSubscription_BringsBoth_WithTheCardSummary()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync();
        var subscription = NewId("sub");
        var invoice = NewId("in");
        _api.Payments.Subscriptions[subscription] = Subscription(subscription, plan.PriceId, SubscriptionStatus.Active, user);
        _api.Payments.Invoices[invoice] = Invoice(invoice, subscription, ProviderInvoiceStatus.Paid, attempted: true, "visa", "4242");

        Assert.Equal(HttpStatusCode.OK, (await DeliverAsync(Event("invoice.paid", invoice))).StatusCode);

        var stored = await InvoiceAsync(invoice);
        Assert.Equal((InvoiceStatus.Paid, 9999L, "TRY", "visa", "4242", user), (stored.Status, stored.AmountMinor, stored.Currency, stored.CardBrand, stored.CardLast4, stored.UserId));
        Assert.Equal((await SubscriptionAsync(subscription)).Id, stored.SubscriptionId);
    }

    [Fact]
    public async Task AFailedPayment_ThenASuccessfulOne()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync();
        var subscription = NewId("sub");
        var invoice = NewId("in");
        _api.Payments.Subscriptions[subscription] = Subscription(subscription, plan.PriceId, SubscriptionStatus.PastDue, user);
        _api.Payments.Invoices[invoice] = Invoice(invoice, subscription, ProviderInvoiceStatus.Open, attempted: true);

        await DeliverAsync(Event("invoice.payment_failed", invoice));
        Assert.Equal(InvoiceStatus.PaymentFailed, (await InvoiceAsync(invoice)).Status);
        Assert.Equal(SubscriptionStatus.PastDue, (await SubscriptionAsync(subscription)).Status);

        _api.Payments.Subscriptions[subscription] = Subscription(subscription, plan.PriceId, SubscriptionStatus.Active, user);
        _api.Payments.Invoices[invoice] = Invoice(invoice, subscription, ProviderInvoiceStatus.Paid, attempted: true, "mastercard", "4444");
        await DeliverAsync(Event("invoice.paid", invoice));

        Assert.Equal(InvoiceStatus.Paid, (await InvoiceAsync(invoice)).Status);
        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync(subscription)).Status);
    }

    [Fact]
    public async Task AVoidedInvoice_IsVoid_AndAPaidOneStaysPaid()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync();
        var subscription = NewId("sub");
        var voided = NewId("in");
        var paid = NewId("in");
        _api.Payments.Subscriptions[subscription] = Subscription(subscription, plan.PriceId, SubscriptionStatus.Active, user);
        _api.Payments.Invoices[voided] = Invoice(voided, subscription, ProviderInvoiceStatus.Void, attempted: false);
        _api.Payments.Invoices[paid] = Invoice(paid, subscription, ProviderInvoiceStatus.Paid, attempted: true);

        await DeliverAsync(Event("invoice.voided", voided));
        await DeliverAsync(Event("invoice.paid", paid));
        _api.Payments.Invoices[paid] = Invoice(paid, subscription, ProviderInvoiceStatus.Void, attempted: true);
        await DeliverAsync(Event("invoice.voided", paid));

        Assert.Equal(InvoiceStatus.Void, (await InvoiceAsync(voided)).Status);
        Assert.Equal(InvoiceStatus.Paid, (await InvoiceAsync(paid)).Status);
    }

    [Fact]
    public async Task ACompletedCheckout_LeadsToItsSubscription()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync();
        var subscription = NewId("sub");
        _api.Payments.Subscriptions[subscription] = Subscription(subscription, plan.PriceId, SubscriptionStatus.Active, user);
        var payload = Event("checkout.session.completed", NewId("cs"), "checkout.session", extra: $",\"mode\":\"subscription\",\"subscription\":\"{subscription}\"");

        Assert.Equal(HttpStatusCode.OK, (await DeliverAsync(payload)).StatusCode);
        Assert.Equal(user, (await SubscriptionAsync(subscription)).UserId);
    }

    [Fact]
    public async Task WithoutTheUserInItsMetadata_TheCustomerIdentifiesTheOwner()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync();
        var subscription = NewId("sub");
        var providerCustomer = NewId("cus");
        await using (var scope = _api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            var customer = BillingCustomer.Create(user, $"{user:N}@example.com");
            customer.LinkProvider(providerCustomer);
            context.Customers.Add(customer);
            await context.SaveChangesAsync(Cancellation);
        }

        _api.Payments.Subscriptions[subscription] = Subscription(subscription, plan.PriceId, SubscriptionStatus.Active, userId: null, providerCustomer);
        await DeliverAsync(Event("customer.subscription.created", subscription));

        Assert.Equal(user, (await SubscriptionAsync(subscription)).UserId);
    }

    [Fact]
    public async Task AnUnknownPrice_IsRetried_UntilThePlanExists()
    {
        var user = Guid.CreateVersion7();
        var price = $"price_{Guid.NewGuid():N}";
        var subscription = NewId("sub");
        _api.Payments.Subscriptions[subscription] = Subscription(subscription, price, SubscriptionStatus.Active, user);
        var payload = Event("customer.subscription.created", subscription);

        await AssertProblemAsync(await DeliverAsync(payload), HttpStatusCode.InternalServerError, "Payment.UnknownPrice");

        await PlanAsync(price);
        Assert.Equal(HttpStatusCode.OK, (await DeliverAsync(payload)).StatusCode);
        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync(subscription)).Status);
    }

    [Fact]
    public async Task WhileTheProviderCannotBeReached_TheEventIsRetried()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync();
        var subscription = NewId("sub");
        _api.Payments.Subscriptions[subscription] = Subscription(subscription, plan.PriceId, SubscriptionStatus.Active, user);
        var payload = Event("customer.subscription.created", subscription);

        _api.Payments.FailWith = PaymentErrors.Unavailable;
        await AssertProblemAsync(await DeliverAsync(payload), HttpStatusCode.ServiceUnavailable, "Payment.Unavailable");
        _api.Payments.FailWith = null;

        Assert.Equal(HttpStatusCode.OK, (await DeliverAsync(payload)).StatusCode);
        Assert.Equal(SubscriptionStatus.Active, (await SubscriptionAsync(subscription)).Status);
    }

    [Fact]
    public async Task EventsThisServiceDoesNotFollow_AreAcknowledged()
    {
        var response = await DeliverAsync(Event("charge.refunded", NewId("ch"), "charge"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ASecondLiveSubscription_IsNotKept()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync();
        var first = NewId("sub");
        var second = NewId("sub");
        _api.Payments.Subscriptions[first] = Subscription(first, plan.PriceId, SubscriptionStatus.Active, user);
        _api.Payments.Subscriptions[second] = Subscription(second, plan.PriceId, SubscriptionStatus.Active, user);

        await DeliverAsync(Event("customer.subscription.created", first));
        var response = await DeliverAsync(Event("customer.subscription.created", second));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _api.CreateScope();
        var kept = await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Subscriptions.AsNoTracking().Where(item => item.UserId == user).Select(item => item.ProviderSubscriptionId).ToListAsync(Cancellation);
        Assert.Equal([first], kept);
    }

    [Fact]
    public async Task WithoutAWebhookSecret_TheEndpointIsUnavailable()
    {
        using var stripe = _api.CreateClientWithoutPayments(Guid.CreateVersion7());
        var payload = Event("customer.subscription.created", "sub_x");

        await AssertProblemAsync(await SendAsync(stripe, payload, EventUtility.GenerateSignatureHeader(payload, BillingApiFixture.WebhookSecret)), HttpStatusCode.ServiceUnavailable, "Webhook.Unavailable");
    }

    [Theory]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("trialing", SubscriptionStatus.Active)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("unpaid", SubscriptionStatus.PastDue)]
    [InlineData("paused", SubscriptionStatus.PastDue)]
    [InlineData("incomplete", SubscriptionStatus.Incomplete)]
    [InlineData("canceled", SubscriptionStatus.Canceled)]
    [InlineData("incomplete_expired", SubscriptionStatus.Canceled)]
    public void StripeStatuses_MapToOurs(string stripe, SubscriptionStatus expected) => Assert.Equal(expected, StripePaymentGateway.StatusOf(stripe));

    [Theory]
    [InlineData("paid", ProviderInvoiceStatus.Paid)]
    [InlineData("open", ProviderInvoiceStatus.Open)]
    [InlineData("void", ProviderInvoiceStatus.Void)]
    [InlineData("uncollectible", ProviderInvoiceStatus.Uncollectible)]
    [InlineData("draft", ProviderInvoiceStatus.Draft)]
    public void StripeInvoiceStatuses_MapToOurs(string stripe, ProviderInvoiceStatus expected) => Assert.Equal(expected, StripePaymentGateway.InvoiceStatusOf(stripe));

    private static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private static string Event(string type, string objectId, string objectType = "subscription", string extra = "")
        => JsonSerializer.Serialize(new { id = NewId("evt"), @object = "event", type, data = new { @object = "OBJECT" } })
            .Replace("\"OBJECT\"", $"{{\"id\":\"{objectId}\",\"object\":\"{objectType}\"{extra}}}", StringComparison.Ordinal);

    private static SubscriptionSnapshot Subscription(string id, string priceId, SubscriptionStatus status, Guid? userId, string? customer = null)
        => new(id, customer ?? NewId("cus"), status, priceId, PeriodEnd, false, userId);

    private static InvoiceSnapshot Invoice(string id, string subscription, ProviderInvoiceStatus status, bool attempted, string? brand = null, string? last4 = null)
        => new(id, subscription, 9999, "TRY", status, attempted, PeriodEnd.AddMonths(-1), status == ProviderInvoiceStatus.Paid ? PeriodEnd.AddMonths(-1) : null, brand, last4);

    private async Task<HttpResponseMessage> DeliverAsync(string payload)
    {
        using var stripe = _api.CreateAnonymousClient();
        return await SendAsync(stripe, payload, EventUtility.GenerateSignatureHeader(payload, BillingApiFixture.WebhookSecret));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string payload, string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe") { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", signature);
        return await client.SendAsync(request, Cancellation);
    }

    private async Task<(Guid Id, string PriceId)> PlanAsync(string? price = null)
    {
        using var admin = _api.CreateClient("billing:plans:read", "billing:plans:write");
        price ??= $"price_{Guid.NewGuid():N}";
        var body = new PlanRequest(new Dictionary<string, PlanTextFields> { ["tr"] = new("Temel", null) }, 9999, "TRY", VideoQuality.Sd, 1, price, 0);
        var response = await admin.PostAsJsonAsync("/admin/plans", body, Json, Cancellation);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancellation));
        return ((await response.Content.ReadFromJsonAsync<CreatedResponse>(Json, Cancellation))!.Id, price);
    }

    private async Task<Domain.Subscriptions.Subscription> SubscriptionAsync(string providerId)
    {
        await using var scope = _api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Subscriptions.AsNoTracking().SingleAsync(item => item.ProviderSubscriptionId == providerId, Cancellation);
    }

    private async Task<Domain.Invoices.Invoice> InvoiceAsync(string providerId)
    {
        await using var scope = _api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Invoices.AsNoTracking().SingleAsync(item => item.ProviderInvoiceId == providerId, Cancellation);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.True(response.StatusCode == status, $"{(int)response.StatusCode}: {body}");
        Assert.Contains($"\"code\":\"{code}\"", body, StringComparison.Ordinal);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
