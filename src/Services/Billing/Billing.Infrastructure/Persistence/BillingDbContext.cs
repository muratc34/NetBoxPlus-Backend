using Billing.Domain.Customers;
using Billing.Domain.Invoices;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Billing.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence;

namespace Billing.Infrastructure.Persistence;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : ServiceDbContext(options), IBillingReadModel
{
    /// <summary>Configuration key under <c>ConnectionStrings</c>; the same name in every service.</summary>
    public const string ConnectionStringName = "Database";

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<BillingCustomer> Customers => Set<BillingCustomer>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    IQueryable<Plan> IBillingReadModel.Plans => Plans.AsNoTracking();

    IQueryable<BillingCustomer> IBillingReadModel.Customers => Customers.AsNoTracking();

    IQueryable<Subscription> IBillingReadModel.Subscriptions => Subscriptions.AsNoTracking();

    IQueryable<Invoice> IBillingReadModel.Invoices => Invoices.AsNoTracking();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
}
