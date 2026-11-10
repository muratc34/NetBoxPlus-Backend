using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.API.Admin;
using Billing.Application.Abstractions;
using Billing.Application.Plans;
using Billing.Contracts;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Billing.Infrastructure.Persistence;
using Billing.IntegrationTests.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Stripe;

namespace Billing.IntegrationTests.Webhooks;

/// <summary>What other services hear: outbox messages written with the change, through the real webhook.</summary>
[Collection(nameof(BillingApiCollection))]
public sealed class PublishedEventTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = CreateJson();
    private static readonly DateTimeOffset PeriodEnd = new(2026, 12, 10, 0, 0, 0, TimeSpan.Zero);

    private readonly BillingApiFixture _api;

    public PublishedEventTests(BillingApiFixture api)
    {
        _api = api;
        _api.Payments.Reset();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _api.Payments.Reset();

    [Fact]
    public async Task ASubscriptionsLife_IsPublished_OneEventPerChange()
    {
        var user = Guid.CreateVersion7();
        var basic = await PlanAsync(VideoQuality.Sd, 1);
        var premium = await PlanAsync(VideoQuality.Uhd, 4);
        var id = NewId("sub");

        async Task StateAsync(SubscriptionStatus status, (Guid Id, string PriceId) plan)
        {
            _api.Payments.Subscriptions[id] = new SubscriptionSnapshot(id, NewId("cus"), status, plan.PriceId, PeriodEnd, false, user);
            Assert.Equal(HttpStatusCode.OK, (await DeliverAsync(Event("customer.subscription.updated", id))).StatusCode);
        }

        await StateAsync(SubscriptionStatus.Active, basic);
        await StateAsync(SubscriptionStatus.Active, basic);
        await StateAsync(SubscriptionStatus.PastDue, basic);
        await StateAsync(SubscriptionStatus.Active, basic);
        await StateAsync(SubscriptionStatus.Active, premium);
        await StateAsync(SubscriptionStatus.Canceled, premium);

        var messages = await MessagesAsync(user);
        Assert.Equal(
            [nameof(SubscriptionActivated), nameof(SubscriptionSuspended), nameof(SubscriptionActivated), nameof(SubscriptionActivated), nameof(SubscriptionCanceled)],
            messages.Select(message => message.Type.Split('.').Last()));

        var first = Read<SubscriptionActivated>(messages[0]);
        Assert.Equal((user, basic.Id, "Sd", 1, (DateTimeOffset?)PeriodEnd, 1), (first.UserId, first.PlanId, first.VideoQuality, first.MaxScreens, first.CurrentPeriodEnd, first.Revision));
        var upgraded = Read<SubscriptionActivated>(messages[3]);
        Assert.Equal((premium.Id, "Uhd", 4, 4), (upgraded.PlanId, upgraded.VideoQuality, upgraded.MaxScreens, upgraded.Revision));
        Assert.Equal(2, Read<SubscriptionSuspended>(messages[1]).Revision);
        Assert.Equal(5, Read<SubscriptionCanceled>(messages[4]).Revision);
        Assert.Equal("Billing.Contracts.SubscriptionActivated", messages[0].Type);
    }

    [Fact]
    public async Task AFailedPayment_IsPublishedOnce()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanAsync(VideoQuality.Hd, 2);
        var subscription = NewId("sub");
        var invoice = NewId("in");
        _api.Payments.Subscriptions[subscription] = new SubscriptionSnapshot(subscription, NewId("cus"), SubscriptionStatus.PastDue, plan.PriceId, PeriodEnd, false, user);
        _api.Payments.Invoices[invoice] = new InvoiceSnapshot(invoice, subscription, 14999, "TRY", ProviderInvoiceStatus.Open, true, PeriodEnd, null, null, null);

        await DeliverAsync(Event("invoice.payment_failed", invoice));
        await DeliverAsync(Event("invoice.payment_failed", invoice));

        var failed = Assert.Single(await MessagesAsync(user), message => message.Type.EndsWith(nameof(PaymentFailed), StringComparison.Ordinal));
        var payload = Read<PaymentFailed>(failed);
        Assert.Equal((14999L, "TRY", user), (payload.AmountMinor, payload.Currency, payload.UserId));
    }

    [Fact]
    public async Task AChangeThatIsNotKept_PublishesNothing()
    {
        var user = Guid.CreateVersion7();
        var id = NewId("sub");
        _api.Payments.Subscriptions[id] = new SubscriptionSnapshot(id, NewId("cus"), SubscriptionStatus.Active, $"price_{Guid.NewGuid():N}", PeriodEnd, false, user);

        Assert.Equal(HttpStatusCode.InternalServerError, (await DeliverAsync(Event("customer.subscription.created", id))).StatusCode);

        Assert.Empty(await MessagesAsync(user));
    }

    private static T Read<T>(OutboxView message) => JsonSerializer.Deserialize<T>(message.Payload, IntegrationEvents.SerializerOptions)!;

    private sealed record OutboxView(string Type, string Payload, DateTimeOffset OccurredAt);

    private async Task<List<OutboxView>> MessagesAsync(Guid user)
    {
        await using var scope = _api.CreateScope();
        var all = await scope.ServiceProvider.GetRequiredService<BillingDbContext>().OutboxMessages.AsNoTracking()
            .Where(message => message.Type.StartsWith("Billing.Contracts."))
            .Select(message => new OutboxView(message.Type, message.Payload, message.OccurredAt))
            .ToListAsync(Cancellation);

        // Ordered by the revision they carry, or by time for the ones without.
        return [.. all.Where(message => message.Payload.Contains(user.ToString(), StringComparison.Ordinal))
            .OrderBy(message => JsonDocument.Parse(message.Payload).RootElement.TryGetProperty("revision", out var revision) ? revision.GetInt32() : int.MaxValue)
            .ThenBy(message => message.OccurredAt)];
    }

    private static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private static string Event(string type, string objectId)
        => JsonSerializer.Serialize(new { id = NewId("evt"), @object = "event", type, data = new { @object = new { id = objectId, @object = "object" } } });

    private async Task<HttpResponseMessage> DeliverAsync(string payload)
    {
        using var stripe = _api.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe") { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", EventUtility.GenerateSignatureHeader(payload, BillingApiFixture.WebhookSecret));
        return await stripe.SendAsync(request, Cancellation);
    }

    private async Task<(Guid Id, string PriceId)> PlanAsync(VideoQuality quality, int screens)
    {
        using var admin = _api.CreateClient("billing:plans:read", "billing:plans:write");
        var price = $"price_{Guid.NewGuid():N}";
        var body = new PlanRequest(new Dictionary<string, PlanTextFields> { ["tr"] = new("Plan", null) }, 9999, "TRY", quality, screens, price, 0);
        var response = await admin.PostAsJsonAsync("/admin/plans", body, Json, Cancellation);
        return ((await response.Content.ReadFromJsonAsync<CreatedResponse>(Json, Cancellation))!.Id, price);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
