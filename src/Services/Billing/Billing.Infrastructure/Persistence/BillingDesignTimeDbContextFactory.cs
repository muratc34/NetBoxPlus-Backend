using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Billing.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without starting the API. Adding a
/// migration never connects, so the connection string is a placeholder.
/// </summary>
internal sealed class BillingDesignTimeDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql("Host=design-time-only")
            .Options);
}
