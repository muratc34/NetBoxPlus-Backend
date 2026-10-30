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

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
}
