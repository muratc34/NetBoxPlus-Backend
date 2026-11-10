using Billing.Domain.Customers;
using Billing.Domain.Invoices;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;

namespace Billing.Domain.Tests;

public sealed class PlanTests
{
    private static readonly PlanTerms Terms = new(14999, "TRY", VideoQuality.Hd, 2, "price_1");

    private static Dictionary<string, PlanText> Texts(string? name = "Standart") => new() { ["tr"] = new PlanText(name, "HD.") };

    [Fact]
    public void Create_MakesAPlanThatIsNotOnSale()
    {
        var plan = Plan.Create(Texts(), Terms).Value;

        Assert.False(plan.IsActive);
        Assert.Equal((14999L, "TRY", "price_1"), (plan.AmountMinor, plan.Currency, plan.ProviderPriceId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Create_NeedsAName(string? name) => Assert.Equal(PlanErrors.NameRequired, Plan.Create(Texts(name), Terms).Error);

    [Fact]
    public void Create_NeedsTurkish()
        => Assert.Equal(PlanErrors.NameRequired, Plan.Create(new Dictionary<string, PlanText> { ["en"] = new("Basic", null) }, Terms).Error);

    [Fact]
    public void Create_RefusesUnknownLanguages()
    {
        var texts = Texts();
        texts["fr"] = new PlanText("Standard", null);

        Assert.Equal(PlanErrors.UnsupportedLanguage, Plan.Create(texts, Terms).Error);
    }

    [Fact]
    public void Create_RefusesLongTexts()
    {
        Assert.Equal(PlanErrors.NameTooLong, Plan.Create(Texts(new string('a', Plan.NameMaxLength + 1)), Terms).Error);
        Assert.Equal(PlanErrors.DescriptionTooLong, Plan.Create(new Dictionary<string, PlanText> { ["tr"] = new("A", new string('a', Plan.DescriptionMaxLength + 1)) }, Terms).Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NeedsAPositivePrice(long amount) => Assert.Equal(PlanErrors.AmountInvalid, Plan.Create(Texts(), Terms with { AmountMinor = amount }).Error);

    [Theory]
    [InlineData(null)]
    [InlineData("USD")]
    [InlineData("try")]
    public void Create_OnlyTakesKnownCurrencies(string? currency) => Assert.Equal(PlanErrors.CurrencyUnsupported, Plan.Create(Texts(), Terms with { Currency = currency }).Error);

    [Fact]
    public void Create_RefusesAnUnknownQuality() => Assert.Equal(PlanErrors.QualityInvalid, Plan.Create(Texts(), Terms with { Quality = (VideoQuality)99 }).Error);

    [Theory]
    [InlineData(0)]
    [InlineData(Plan.MaxScreensLimit + 1)]
    public void Create_LimitsScreens(int screens) => Assert.Equal(PlanErrors.ScreensInvalid, Plan.Create(Texts(), Terms with { MaxScreens = screens }).Error);

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Create_NeedsAProviderPrice(string? priceId) => Assert.Equal(PlanErrors.ProviderPriceRequired, Plan.Create(Texts(), Terms with { ProviderPriceId = priceId }).Error);

    [Fact]
    public void Change_ReplacesTexts_AndRaisesTheVersion()
    {
        var texts = Texts();
        texts["en"] = new PlanText("Standard", "HD.");
        var plan = Plan.Create(texts, Terms).Value;
        var version = plan.Version;

        Assert.True(plan.Change(new Dictionary<string, PlanText> { ["tr"] = new("Yeni", null) }, Terms with { AmountMinor = 100 }).IsSuccess);

        Assert.Equal("Yeni", Assert.Single(plan.Translations).Name);
        Assert.Equal(100, plan.AmountMinor);
        Assert.True(plan.Version > version);
    }

    [Fact]
    public void AFailedChange_LeavesThePlanAlone()
    {
        var plan = Plan.Create(Texts(), Terms).Value;
        var version = plan.Version;

        Assert.True(plan.Change(Texts("Yeni"), Terms with { AmountMinor = 0 }).IsFailure);

        Assert.Equal(("Standart", 14999L, version), (Assert.Single(plan.Translations).Name, plan.AmountMinor, plan.Version));
    }

    [Fact]
    public void CheckVersion_AcceptsOnlyTheCurrentVersion()
    {
        var plan = Plan.Create(Texts(), Terms).Value;

        Assert.True(plan.CheckVersion(plan.Version).IsSuccess);
        Assert.Equal(PlanErrors.VersionConflict, plan.CheckVersion(plan.Version - 1).Error);
    }

    [Fact]
    public void TextFor_FallsBackToTurkish()
    {
        var plan = Plan.Create(Texts(), Terms).Value;

        Assert.Equal("tr", plan.TextFor("en").Language);
    }

    [Fact]
    public void TextFor_FallsBackToTurkish_WhenOtherLanguagesExist()
    {
        var texts = Texts();
        texts["en"] = new PlanText("Standard", "HD.");
        var plan = Plan.Create(texts, Terms).Value;

        Assert.Equal("tr", plan.TextFor("de").Language);
    }

    [Fact]
    public void ActivateAndDeactivate_ChangeTheVersionOnlyWhenTheyChangeSomething()
    {
        var plan = Plan.Create(Texts(), Terms).Value;
        plan.Activate();
        var version = plan.Version;

        plan.Activate();
        Assert.Equal(version, plan.Version);

        plan.Deactivate();
        Assert.False(plan.IsActive);
        Assert.True(plan.Version > version);
    }
}

public sealed class SubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PlanId = Guid.NewGuid();

    private static SubscriptionState State(SubscriptionStatus status, bool cancelAtEnd = false) => new(status, PlanId, Now.AddMonths(1), cancelAtEnd);

    [Fact]
    public void Start_TakesTheProvidersState()
    {
        var subscription = Subscription.Start(Guid.NewGuid(), "sub_1", State(SubscriptionStatus.Active), Now);

        Assert.Equal((SubscriptionStatus.Active, PlanId, Now.AddMonths(1)), (subscription.Status, subscription.PlanId, subscription.CurrentPeriodEnd));
        Assert.Null(subscription.CanceledAt);
    }

    [Fact]
    public void Apply_FollowsTheProvider()
    {
        var subscription = Subscription.Start(Guid.NewGuid(), "sub_1", State(SubscriptionStatus.Active), Now);
        var other = Guid.NewGuid();

        subscription.Apply(new SubscriptionState(SubscriptionStatus.PastDue, other, Now.AddMonths(2), true), Now);

        Assert.Equal((SubscriptionStatus.PastDue, other, Now.AddMonths(2), true), (subscription.Status, subscription.PlanId, subscription.CurrentPeriodEnd, subscription.CancelAtPeriodEnd));
    }

    [Fact]
    public void Canceling_RecordsWhen()
    {
        var subscription = Subscription.Start(Guid.NewGuid(), "sub_1", State(SubscriptionStatus.Active), Now);

        subscription.Apply(State(SubscriptionStatus.Canceled), Now.AddDays(3));

        Assert.Equal((SubscriptionStatus.Canceled, Now.AddDays(3)), (subscription.Status, subscription.CanceledAt));
    }

    [Fact]
    public void ACanceledSubscription_CannotComeBack()
    {
        var subscription = Subscription.Start(Guid.NewGuid(), "sub_1", State(SubscriptionStatus.Active), Now);
        subscription.Apply(State(SubscriptionStatus.Canceled), Now);

        subscription.Apply(State(SubscriptionStatus.Active), Now.AddDays(1));

        Assert.Equal((SubscriptionStatus.Canceled, Now), (subscription.Status, subscription.CanceledAt));
    }
}

public sealed class SubscriptionEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Basic = Guid.NewGuid();
    private static readonly Guid Premium = Guid.NewGuid();

    private static SubscriptionState State(SubscriptionStatus status, Guid? plan = null, DateTimeOffset? end = null) => new(status, plan ?? Basic, end ?? Now.AddMonths(1), false);

    [Fact]
    public void Starting_Active_RaisesActivated_WithRevisionOne()
    {
        var subscription = Subscription.Start(Guid.NewGuid(), "sub_1", State(SubscriptionStatus.Active), Now);

        var activated = Assert.IsType<SubscriptionActivatedDomainEvent>(Assert.Single(subscription.DomainEvents));
        Assert.Equal((subscription.Id, Basic, 1, (DateTimeOffset?)Now.AddMonths(1)), (activated.SubscriptionId, activated.PlanId, activated.Revision, activated.CurrentPeriodEnd));
        Assert.Equal(1, subscription.Revision);
    }

    [Fact]
    public void Starting_Incomplete_RaisesNothing()
    {
        var subscription = Subscription.Start(Guid.NewGuid(), "sub_1", State(SubscriptionStatus.Incomplete), Now);

        Assert.Empty(subscription.DomainEvents);
    }

    [Fact]
    public void TheSameStateAgain_OrARenewal_IsNoNews()
    {
        var subscription = Subscription.Start(Guid.NewGuid(), "sub_1", State(SubscriptionStatus.Active), Now);
        subscription.ClearDomainEvents();

        subscription.Apply(State(SubscriptionStatus.Active), Now);
        subscription.Apply(State(SubscriptionStatus.Active, end: Now.AddMonths(2)), Now);

        Assert.Empty(subscription.DomainEvents);
        Assert.Equal(1, subscription.Revision);
        Assert.Equal(Now.AddMonths(2), subscription.CurrentPeriodEnd);
    }

    [Fact]
    public void EveryChange_RaisesItsEvent_WithARisingRevision()
    {
        var subscription = Subscription.Start(Guid.NewGuid(), "sub_1", State(SubscriptionStatus.Active), Now);

        subscription.Apply(State(SubscriptionStatus.PastDue), Now);
        subscription.Apply(State(SubscriptionStatus.Active), Now);
        subscription.Apply(State(SubscriptionStatus.Active, Premium), Now);
        subscription.Apply(State(SubscriptionStatus.Canceled, Premium), Now.AddDays(1));

        Assert.Collection(
            subscription.DomainEvents,
            item => Assert.Equal(1, Assert.IsType<SubscriptionActivatedDomainEvent>(item).Revision),
            item => Assert.Equal(2, Assert.IsType<SubscriptionSuspendedDomainEvent>(item).Revision),
            item => Assert.Equal(3, Assert.IsType<SubscriptionActivatedDomainEvent>(item).Revision),
            item => Assert.Equal((4, Premium), (Assert.IsType<SubscriptionActivatedDomainEvent>(item).Revision, ((SubscriptionActivatedDomainEvent)item).PlanId)),
            item => Assert.Equal((5, Now.AddDays(1)), (Assert.IsType<SubscriptionCanceledDomainEvent>(item).Revision, ((SubscriptionCanceledDomainEvent)item).CanceledAt)));
    }

    [Fact]
    public void APlanChangeWhileSuspended_IsNotASecondSuspension()
    {
        var subscription = Subscription.Start(Guid.NewGuid(), "sub_1", State(SubscriptionStatus.PastDue), Now);
        subscription.ClearDomainEvents();

        subscription.Apply(State(SubscriptionStatus.PastDue, Premium), Now);

        Assert.Empty(subscription.DomainEvents);
        Assert.Equal(2, subscription.Revision);
    }

    [Fact]
    public void ACanceledSubscription_RaisesNothingMore()
    {
        var subscription = Subscription.Start(Guid.NewGuid(), "sub_1", State(SubscriptionStatus.Canceled), Now);
        subscription.ClearDomainEvents();

        subscription.Apply(State(SubscriptionStatus.Active), Now);

        Assert.Empty(subscription.DomainEvents);
    }

    [Fact]
    public void AFirstPaymentFailure_IsAnEvent_ARepeatIsNot()
    {
        var invoice = Invoice.Record(Guid.NewGuid(), Guid.NewGuid(), "in_1", 14999, "TRY", Now);

        invoice.MarkPaymentFailed();
        invoice.MarkPaymentFailed();

        var failed = Assert.IsType<InvoicePaymentFailedDomainEvent>(Assert.Single(invoice.DomainEvents));
        Assert.Equal((invoice.Id, 14999L, "TRY"), (failed.InvoiceId, failed.AmountMinor, failed.Currency));
    }
}

public sealed class InvoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static Invoice NewInvoice() => Invoice.Record(Guid.NewGuid(), Guid.NewGuid(), "in_1", 14999, "TRY", Now);

    [Theory]
    [InlineData("4242424242424242")]
    [InlineData("424")]
    [InlineData("42a2")]
    [InlineData("")]
    [InlineData(null)]
    public void CardSummary_RefusesAnythingButFourDigits(string? last4) => Assert.Equal(InvoiceErrors.CardInvalid, CardSummary.Create("visa", last4).Error);

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void CardSummary_NeedsABrand(string? brand) => Assert.Equal(InvoiceErrors.CardInvalid, CardSummary.Create(brand, "4242").Error);

    [Fact]
    public void AFailedInvoice_CanStillBePaid()
    {
        var invoice = NewInvoice();
        invoice.MarkPaymentFailed();
        Assert.Equal(InvoiceStatus.PaymentFailed, invoice.Status);

        invoice.MarkPaid(Now, CardSummary.Create("visa", "4242").Value);

        Assert.Equal((InvoiceStatus.Paid, Now, "visa", "4242"), (invoice.Status, invoice.PaidAt, invoice.CardBrand, invoice.CardLast4));
    }

    [Fact]
    public void Paid_IsFinal()
    {
        var invoice = NewInvoice();
        invoice.MarkPaid(Now, null);

        invoice.MarkPaymentFailed();
        invoice.MarkVoid();

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public void PayingTwice_KeepsTheFirstPayment()
    {
        var invoice = NewInvoice();
        invoice.MarkPaid(Now, null);

        invoice.MarkPaid(Now.AddDays(1), CardSummary.Create("visa", "4242").Value);

        Assert.Equal(Now, invoice.PaidAt);
        Assert.Null(invoice.CardLast4);
    }

    [Fact]
    public void Void_AppliesToUnpaidInvoices()
    {
        var invoice = NewInvoice();

        invoice.MarkVoid();

        Assert.Equal(InvoiceStatus.Void, invoice.Status);
    }
}

public sealed class BillingCustomerTests
{
    [Fact]
    public void LinkProvider_AcceptsTheSameCustomerAgain()
    {
        var customer = BillingCustomer.Create(Guid.NewGuid(), "a@example.com");

        Assert.True(customer.LinkProvider("cus_1").IsSuccess);
        Assert.True(customer.LinkProvider("cus_1").IsSuccess);
        Assert.Equal("cus_1", customer.ProviderCustomerId);
    }

    [Fact]
    public void LinkProvider_RefusesADifferentCustomer()
    {
        var customer = BillingCustomer.Create(Guid.NewGuid(), "a@example.com");
        customer.LinkProvider("cus_1");

        Assert.Equal(CustomerErrors.ProviderMismatch, customer.LinkProvider("cus_2").Error);
        Assert.Equal("cus_1", customer.ProviderCustomerId);
    }
}
