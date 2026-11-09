using Billing.Domain.Customers;
using Billing.Domain.Invoices;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Billing.Infrastructure.Persistence;

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    private const int LanguageMaxLength = 8;
    private const int EnumMaxLength = 16;

    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("Plans");
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.Id).ValueGeneratedNever();
        builder.OwnsMany(plan => plan.Translations, translation =>
        {
            translation.ToTable("PlanTranslations");
            translation.WithOwner().HasForeignKey("PlanId");
            translation.HasKey("PlanId", nameof(PlanTranslation.Language));
            translation.Property(item => item.Language).HasMaxLength(LanguageMaxLength);
            translation.Property(item => item.Name).HasMaxLength(Plan.NameMaxLength).IsRequired();
            translation.Property(item => item.Description).HasMaxLength(Plan.DescriptionMaxLength).IsRequired();
        });
        builder.Navigation(plan => plan.Translations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(plan => plan.Currency).HasMaxLength(Plan.CurrencyLength).IsRequired();
        builder.Property(plan => plan.Quality).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(plan => plan.ProviderPriceId).HasMaxLength(Plan.ProviderPriceIdMaxLength).IsRequired();
        builder.HasIndex(plan => plan.ProviderPriceId).IsUnique();
        builder.Property(plan => plan.Version).IsConcurrencyToken();
    }
}

internal sealed class BillingCustomerConfiguration : IEntityTypeConfiguration<BillingCustomer>
{
    public void Configure(EntityTypeBuilder<BillingCustomer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).ValueGeneratedNever();
        builder.Property(customer => customer.Email).HasMaxLength(BillingCustomer.EmailMaxLength).IsRequired();
        builder.Property(customer => customer.ProviderCustomerId).HasMaxLength(BillingCustomer.ProviderCustomerIdMaxLength);
        builder.HasIndex(customer => customer.UserId).IsUnique();
        builder.HasIndex(customer => customer.ProviderCustomerId).IsUnique();
    }
}

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    private const int StatusMaxLength = 16;

    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions");
        builder.HasKey(subscription => subscription.Id);
        builder.Property(subscription => subscription.Id).ValueGeneratedNever();
        builder.Property(subscription => subscription.ProviderSubscriptionId).HasMaxLength(Subscription.ProviderSubscriptionIdMaxLength).IsRequired();
        builder.Property(subscription => subscription.Status).HasConversion<string>().HasMaxLength(StatusMaxLength);
        builder.HasIndex(subscription => subscription.ProviderSubscriptionId).IsUnique();

        // The database is what guarantees a single live subscription, whatever messages race.
        builder.HasIndex(subscription => subscription.UserId).IsUnique().HasFilter("\"Status\" <> 'Canceled'");
        builder.HasOne<Plan>().WithMany().HasForeignKey(subscription => subscription.PlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    private const int StatusMaxLength = 16;

    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.HasKey(invoice => invoice.Id);
        builder.Property(invoice => invoice.Id).ValueGeneratedNever();
        builder.Property(invoice => invoice.ProviderInvoiceId).HasMaxLength(Invoice.ProviderInvoiceIdMaxLength).IsRequired();
        builder.Property(invoice => invoice.Currency).HasMaxLength(Plan.CurrencyLength).IsRequired();
        builder.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(StatusMaxLength);
        builder.Property(invoice => invoice.CardBrand).HasMaxLength(CardSummary.BrandMaxLength);
        builder.Property(invoice => invoice.CardLast4).HasMaxLength(CardSummary.Last4Length);
        builder.HasIndex(invoice => invoice.ProviderInvoiceId).IsUnique();
        builder.HasIndex(invoice => new { invoice.UserId, invoice.IssuedAt });
        builder.HasOne<Subscription>().WithMany().HasForeignKey(invoice => invoice.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}
