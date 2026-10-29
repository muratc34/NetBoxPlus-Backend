using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Feed.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without starting the API. Adding a
/// migration never connects, so the connection string is a placeholder.
/// </summary>
internal sealed class FeedDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FeedDbContext>
{
    public FeedDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<FeedDbContext>()
            .UseNpgsql("Host=design-time-only")
            .Options);
}
