using Auth.Application.Abstractions;
using Auth.Domain.RefreshTokens;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence;

namespace Auth.Infrastructure.Persistence;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : ServiceDbContext(options), IAuthReadModel
{
    /// <summary>Configuration key under <c>ConnectionStrings</c>; the same name in every service.</summary>
    public const string ConnectionStringName = "Database";

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    IQueryable<User> IAuthReadModel.Users => Users.AsNoTracking();

    IQueryable<Role> IAuthReadModel.Roles => Roles.AsNoTracking();

    // SQL rather than LINQ: the address is stored through a value conversion, which LINQ cannot
    // search inside. Stored addresses are lower case, so the fragment is lowered too.
    IQueryable<User> IAuthReadModel.UsersWithEmailContaining(string fragment)
    {
        var pattern = "%" + EscapeLike(fragment.Trim().ToLowerInvariant()) + "%";
        return Users.FromSql($"SELECT * FROM \"Users\" WHERE \"Email\" LIKE {pattern} ESCAPE '!'").AsNoTracking();
    }

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);

    private static string EscapeLike(string value)
        => value.Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal);
}
