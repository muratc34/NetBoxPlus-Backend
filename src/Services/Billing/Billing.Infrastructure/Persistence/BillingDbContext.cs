using Billing.Domain.Customers;
using Billing.Domain.Invoices;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence;

namespace Billing.Infrastructure.Persistence;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : ServiceDbContext(options)
{
    /// <summary>Configuration key under <c>ConnectionStrings</c>; the same name in every service.</summary>
    public const string ConnectionStringName = "Database";

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<BillingCustomer> Customers => Set<BillingCustomer>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
}
