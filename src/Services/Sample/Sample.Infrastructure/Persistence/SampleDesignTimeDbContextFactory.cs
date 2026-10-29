using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sample.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without starting the API. Adding a
/// migration never connects, so the connection string is a placeholder.
/// </summary>
internal sealed class SampleDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SampleDbContext>
{
    public SampleDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<SampleDbContext>()
            .UseNpgsql("Host=design-time-only")
            .Options);
}
