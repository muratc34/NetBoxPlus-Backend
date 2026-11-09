using Billing.Domain.Customers;
using Billing.Domain.Invoices;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.IntegrationTests;

[Collection(nameof(BillingDatabaseCollection))]
public sealed class PersistenceTests(BillingDatabaseFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Plan_IsStoredWithItsTranslations()
    {
        await using var services = await database.CreateServicesAsync();
        var plan = NewPlan("price_a");
        plan.Activate();
        await SaveAsync(services, context => context.Add(plan));

        await using var scope = services.CreateAsyncScope();
        var found = await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Plans.SingleAsync(item => item.Id == plan.Id, Cancellation);

        Assert.Equal((14999L, "TRY", VideoQuality.Hd, 2, "price_a", true), (found.AmountMinor, found.Currency, found.Quality, found.MaxScreens, found.ProviderPriceId, found.IsActive));
        Assert.Equal("Standard", found.TextFor(Languages.English).Name);
        Assert.Equal(plan.Version, found.Version);
    }

    [Fact]
    public async Task SubscriptionAndInvoice_AreStored()
    {
        await using var services = await database.CreateServicesAsync();
        var plan = NewPlan("price_b");
        var subscription = NewSubscription(Guid.NewGuid(), plan.Id, "sub_b");
        var invoice = Invoice.Record(subscription.UserId, subscription.Id, "in_b", 14999, "TRY", Now);
        invoice.MarkPaid(Now, CardSummary.Create("visa", "4242").Value);
        await SaveAsync(services, context => context.AddRange(plan, subscription, invoice));

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var foundSubscription = await context.Subscriptions.SingleAsync(item => item.Id == subscription.Id, Cancellation);
        var foundInvoice = await context.Invoices.SingleAsync(item => item.Id == invoice.Id, Cancellation);

        Assert.Equal(SubscriptionStatus.Active, foundSubscription.Status);
        Assert.Equal(Now.AddMonths(1), foundSubscription.CurrentPeriodEnd);
        Assert.Equal((InvoiceStatus.Paid, "visa", "4242"), (foundInvoice.Status, foundInvoice.CardBrand, foundInvoice.CardLast4));
    }

    [Fact]
    public async Task AUser_CannotHaveTwoLiveSubscriptions()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.NewGuid();
        var plan = NewPlan("price_c");
        await SaveAsync(services, context => context.AddRange(plan, NewSubscription(user, plan.Id, "sub_c1")));

        await Assert.ThrowsAnyAsync<Exception>(() => SaveAsync(services, context => context.Add(NewSubscription(user, plan.Id, "sub_c2"))));
    }

    [Fact]
    public async Task AUser_CanSubscribeAgain_AfterCanceling()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.NewGuid();
        var plan = NewPlan("price_d");
        var canceled = NewSubscription(user, plan.Id, "sub_d1");
        canceled.Apply(new SubscriptionState(SubscriptionStatus.Canceled, plan.Id, null, false), Now);
        await SaveAsync(services, context => context.AddRange(plan, canceled));

        await SaveAsync(services, context => context.Add(NewSubscription(user, plan.Id, "sub_d2")));

        await using var scope = services.CreateAsyncScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Subscriptions.CountAsync(item => item.UserId == user, Cancellation));
    }

    [Fact]
    public async Task ProviderIds_AndUsers_AreUnique()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.NewGuid();
        await SaveAsync(services, context => context.Add(BillingCustomer.Create(user, "a@example.com")));

        await Assert.ThrowsAnyAsync<Exception>(() => SaveAsync(services, context => context.Add(BillingCustomer.Create(user, "b@example.com"))));

        await SaveAsync(services, context => context.Add(NewPlan("price_e")));
        await Assert.ThrowsAnyAsync<Exception>(() => SaveAsync(services, context => context.Add(NewPlan("price_e"))));
    }

    [Fact]
    public async Task Model_HasNoPendingChanges()
    {
        await using var services = await database.CreateServicesAsync();
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();

        Assert.False(context.Database.HasPendingModelChanges());
        Assert.NotNull(context.GetService<IMigrator>());
    }

    private static Plan NewPlan(string priceId) => Plan.Create(
        new Dictionary<string, PlanText> { ["tr"] = new("Standart", "HD izleme."), ["en"] = new("Standard", "Watch in HD.") },
        new PlanTerms(14999, "TRY", VideoQuality.Hd, 2, priceId)).Value;

    private static Subscription NewSubscription(Guid user, Guid plan, string providerId)
        => Subscription.Start(user, providerId, new SubscriptionState(SubscriptionStatus.Active, plan, Now.AddMonths(1), false), Now);

    private static async Task SaveAsync(ServiceProvider services, Action<BillingDbContext> change)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        change(context);
        await context.SaveChangesAsync(Cancellation);
    }
}
