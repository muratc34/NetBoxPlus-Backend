using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without starting the API. Adding a
/// migration never connects, so the connection string is a placeholder.
/// </summary>
internal sealed class AuthDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql("Host=design-time-only")
            .Options);
}
