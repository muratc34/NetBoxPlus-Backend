using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.API.Admin;
using Billing.Application.Abstractions;
using Billing.Application.Checkout.OpenPortal;
using Billing.Application.Checkout.StartCheckout;
using Billing.Application.Plans;
using Billing.Domain.Customers;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Billing.Infrastructure.Idempotency;
using Billing.Infrastructure.Persistence;
using Billing.IntegrationTests.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.IntegrationTests.Checkout;

[Collection(nameof(BillingApiCollection))]
public sealed class CheckoutApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = CreateJson();

    private readonly BillingApiFixture _api;

    public CheckoutApiTests(BillingApiFixture api)
    {
        _api = api;
        _api.Payments.Reset();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _api.Payments.Reset();

    [Fact]
    public async Task Checkout_OpensTheProvidersPage_AndCreatesTheProviderCustomerOnce()
    {
        var user = Guid.CreateVersion7();
        await CustomerAsync(user);
        var plan = await PlanOnSaleAsync();
        using var client = _api.CreateClientFor(user);

        var first = await CheckoutAsync(client, plan, "key-1");
        var second = await CheckoutAsync(client, plan, "key-2");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.StartsWith("https://checkout.test/", (await ReadAsync<CheckoutSession>(first)).Url.AbsoluteUri, StringComparison.Ordinal);
        Assert.Single(_api.Payments.CustomersCreated, id => id == user);
        Assert.Equal($"cus_{user:N}", (await LoadCustomerAsync(user)).ProviderCustomerId);

        var request = _api.Payments.Checkouts.First(item => item.UserId == user);
        Assert.Equal((plan.Id, plan.PriceId, $"cus_{user:N}", "key-1"), (request.PlanId, request.ProviderPriceId, request.ProviderCustomerId, request.IdempotencyKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a key with ünicode")]
    public async Task Checkout_NeedsAUsableIdempotencyKey(string? key)
    {
        var user = Guid.CreateVersion7();
        await CustomerAsync(user);
        var plan = await PlanOnSaleAsync();
        using var client = _api.CreateClientFor(user);

        var response = await CheckoutAsync(client, plan, key);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "Idempotency.KeyRequired");
        Assert.DoesNotContain(_api.Payments.Checkouts, item => item.UserId == user);
    }

    [Fact]
    public async Task Checkout_FollowsTheRules()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanOnSaleAsync();
        var offSale = await PlanAsync(active: false);
        using var client = _api.CreateClientFor(user);
        using var anonymous = _api.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await CheckoutAsync(anonymous, plan, "k")).StatusCode);
        await AssertProblemAsync(await CheckoutAsync(client, plan, "k-ready"), HttpStatusCode.Conflict, "Customer.NotReady");

        await CustomerAsync(user);
        await AssertProblemAsync(await CheckoutAsync(client, offSale, "k-off"), HttpStatusCode.NotFound, "Plan.NotFound");
        await AssertProblemAsync(await CheckoutAsync(client, (Guid.CreateVersion7(), "price_none"), "k-unknown"), HttpStatusCode.NotFound, "Plan.NotFound");

        await LiveSubscriptionAsync(user, plan.Id);
        await AssertProblemAsync(await CheckoutAsync(client, plan, "k-live"), HttpStatusCode.Conflict, "Subscription.AlreadyActive");
        Assert.DoesNotContain(_api.Payments.Checkouts, item => item.UserId == user);
    }

    [Fact]
    public async Task TheSameRequestAgain_GetsTheFirstAnswer_WithoutASecondSession()
    {
        var user = Guid.CreateVersion7();
        await CustomerAsync(user);
        var plan = await PlanOnSaleAsync();
        using var client = _api.CreateClientFor(user);

        var first = await CheckoutAsync(client, plan, "same");
        var again = await CheckoutAsync(client, plan, "same");

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(Cancellation), await again.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal("true", Assert.Single(again.Headers.GetValues("Idempotent-Replayed")));
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        Assert.Single(_api.Payments.Checkouts, item => item.UserId == user);
    }

    [Fact]
    public async Task AKeyReusedForAnotherRequest_IsRefused()
    {
        var user = Guid.CreateVersion7();
        await CustomerAsync(user);
        var plan = await PlanOnSaleAsync();
        var other = await PlanOnSaleAsync();
        using var client = _api.CreateClientFor(user);

        Assert.Equal(HttpStatusCode.OK, (await CheckoutAsync(client, plan, "reused")).StatusCode);
        await AssertProblemAsync(await CheckoutAsync(client, other, "reused"), HttpStatusCode.UnprocessableEntity, "Idempotency.KeyReused");
        await AssertProblemAsync(await PortalAsync(client, "reused"), HttpStatusCode.UnprocessableEntity, "Idempotency.KeyReused");
    }

    [Fact]
    public async Task WhileTheFirstRequestRuns_TheSameKeyWaits()
    {
        var user = Guid.CreateVersion7();
        await CustomerAsync(user);
        var plan = await PlanOnSaleAsync();
        using var client = _api.CreateClientFor(user);
        using var second = _api.CreateClientFor(user);
        _api.Payments.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = CheckoutAsync(client, plan, "busy");
        await _api.Payments.CheckoutStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        var meanwhile = await CheckoutAsync(second, plan, "busy");
        _api.Payments.Gate.SetResult();

        await AssertProblemAsync(meanwhile, HttpStatusCode.Conflict, "Idempotency.InProgress");
        Assert.Equal(HttpStatusCode.OK, (await running).StatusCode);
        Assert.Single(_api.Payments.Checkouts, item => item.UserId == user);
    }

    [Fact]
    public async Task AFailedRequest_FreesItsKey()
    {
        var user = Guid.CreateVersion7();
        var plan = await PlanOnSaleAsync();
        using var client = _api.CreateClientFor(user);

        await AssertProblemAsync(await CheckoutAsync(client, plan, "retry"), HttpStatusCode.Conflict, "Customer.NotReady");
        await CustomerAsync(user);

        Assert.Equal(HttpStatusCode.OK, (await CheckoutAsync(client, plan, "retry")).StatusCode);
    }

    [Fact]
    public async Task ProviderTrouble_IsReported_AndTheKeyCanBeUsedAgain()
    {
        var user = Guid.CreateVersion7();
        await CustomerAsync(user);
        var plan = await PlanOnSaleAsync();
        using var client = _api.CreateClientFor(user);

        _api.Payments.FailWith = PaymentErrors.Unavailable;
        await AssertProblemAsync(await CheckoutAsync(client, plan, "trouble"), HttpStatusCode.ServiceUnavailable, "Payment.Unavailable");
        _api.Payments.FailWith = PaymentErrors.ProviderRejected;
        await AssertProblemAsync(await CheckoutAsync(client, plan, "trouble"), HttpStatusCode.BadGateway, "Payment.ProviderRejected");
        _api.Payments.FailWith = null;

        Assert.Equal(HttpStatusCode.OK, (await CheckoutAsync(client, plan, "trouble")).StatusCode);
    }

    [Fact]
    public async Task Keys_BelongToTheirUser()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        await CustomerAsync(first);
        await CustomerAsync(second);
        var plan = await PlanOnSaleAsync();
        using var one = _api.CreateClientFor(first);
        using var two = _api.CreateClientFor(second);

        Assert.Equal(HttpStatusCode.OK, (await CheckoutAsync(one, plan, "shared")).StatusCode);
        var other = await CheckoutAsync(two, plan, "shared");

        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.False(other.Headers.Contains("Idempotent-Replayed"));
        Assert.Single(_api.Payments.Checkouts, item => item.UserId == second);
    }

    [Fact]
    public async Task Portal_NeedsAProviderCustomer()
    {
        var user = Guid.CreateVersion7();
        await CustomerAsync(user);
        using var client = _api.CreateClientFor(user);

        await AssertProblemAsync(await PortalAsync(client, "portal-1"), HttpStatusCode.Conflict, "Subscription.None");

        await CustomerAsync(user, provider: $"cus_portal_{user:N}");
        var response = await PortalAsync(client, "portal-2");

        Assert.Equal(new Uri($"https://portal.test/cus_portal_{user:N}"), (await ReadAsync<PortalSession>(response)).Url);
        await AssertProblemAsync(await PortalAsync(client, null), HttpStatusCode.BadRequest, "Idempotency.KeyRequired");
    }

    [Fact]
    public async Task WithoutAStripeKey_PaymentsAreUnavailable()
    {
        var user = Guid.CreateVersion7();
        await CustomerAsync(user);
        var plan = await PlanOnSaleAsync();
        using var client = _api.CreateClientWithoutPayments(user);

        await AssertProblemAsync(await CheckoutAsync(client, plan, "no-stripe"), HttpStatusCode.ServiceUnavailable, "Payment.Unavailable");
    }

    [Fact]
    public async Task AnAbandonedRequest_DoesNotHoldItsKeyForever_AndOldKeysAreCleanedUp()
    {
        var user = Guid.CreateVersion7();
        await CustomerAsync(user);
        var plan = await PlanOnSaleAsync();
        using var client = _api.CreateClientFor(user);

        await using (var scope = _api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            context.Set<IdempotencyRequest>().AddRange(
                new IdempotencyRequest { UserId = user, Key = "abandoned", RequestHash = "x", StartedAt = DateTimeOffset.UtcNow.AddMinutes(-3) },
                new IdempotencyRequest { UserId = user, Key = "old", RequestHash = "x", Completed = true, StatusCode = 200, ResponseBody = "{}", StartedAt = DateTimeOffset.UtcNow.AddDays(-2) });
            await context.SaveChangesAsync(Cancellation);
        }

        Assert.Equal(HttpStatusCode.OK, (await CheckoutAsync(client, plan, "abandoned")).StatusCode);

        await using (var scope = _api.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IdempotencyCleaner>().CleanAsync(Cancellation) >= 1);
            var keys = await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Set<IdempotencyRequest>()
                .Where(request => request.UserId == user).Select(request => request.Key).ToListAsync(Cancellation);
            Assert.Equal(["abandoned"], keys);
        }
    }

    private static async Task<HttpResponseMessage> CheckoutAsync(HttpClient client, (Guid Id, string PriceId) plan, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/checkout-sessions") { Content = JsonContent.Create(new { planId = plan.Id }) };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return await client.SendAsync(request, Cancellation);
    }

    private static async Task<HttpResponseMessage> PortalAsync(HttpClient client, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/portal-sessions");
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return await client.SendAsync(request, Cancellation);
    }

    private Task<(Guid Id, string PriceId)> PlanOnSaleAsync() => PlanAsync(active: true);

    private async Task<(Guid Id, string PriceId)> PlanAsync(bool active)
    {
        using var admin = _api.CreateClient("billing:plans:read", "billing:plans:write");
        var price = $"price_{Guid.NewGuid():N}";
        var body = new PlanRequest(new Dictionary<string, PlanTextFields> { ["tr"] = new("Temel", null) }, 9999, "TRY", VideoQuality.Sd, 1, price, 0);
        var id = (await ReadAsync<CreatedResponse>(await admin.PostAsJsonAsync("/admin/plans", body, Json, Cancellation))).Id;
        if (active)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/admin/plans/{id}/activate", null, Cancellation)).StatusCode);
        }

        return (id, price);
    }

    private async Task CustomerAsync(Guid user, string? provider = null)
    {
        await using var scope = _api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var customer = await context.Customers.FirstOrDefaultAsync(item => item.UserId == user, Cancellation);
        if (customer is null)
        {
            customer = BillingCustomer.Create(user, $"{user:N}@example.com");
            context.Customers.Add(customer);
        }

        if (provider is not null)
        {
            customer.LinkProvider(provider);
        }

        await context.SaveChangesAsync(Cancellation);
    }

    private async Task LiveSubscriptionAsync(Guid user, Guid plan)
    {
        await using var scope = _api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        context.Subscriptions.Add(Subscription.Start(user, $"sub_{Guid.NewGuid():N}", new SubscriptionState(SubscriptionStatus.Active, plan, null, false), DateTimeOffset.UtcNow));
        await context.SaveChangesAsync(Cancellation);
    }

    private async Task<BillingCustomer> LoadCustomerAsync(Guid user)
    {
        await using var scope = _api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Customers.AsNoTracking().SingleAsync(item => item.UserId == user, Cancellation);
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

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancellation)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Cancellation))!;
    }
}
