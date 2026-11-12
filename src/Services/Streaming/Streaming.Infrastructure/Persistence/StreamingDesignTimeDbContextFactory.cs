using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Streaming.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without starting the API. Adding a
/// migration never connects, so the connection string is a placeholder.
/// </summary>
internal sealed class StreamingDesignTimeDbContextFactory : IDesignTimeDbContextFactory<StreamingDbContext>
{
    public StreamingDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<StreamingDbContext>()
            .UseNpgsql("Host=design-time-only")
            .Options);
}
