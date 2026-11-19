using Auth.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Persistence;

internal sealed class RoleRepository(AuthDbContext context) : IRoleRepository
{
    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Roles.FirstOrDefaultAsync(role => role.Id == id, cancellationToken);

    public Task<Role?> GetSystemRoleAsync(CancellationToken cancellationToken)
        => context.Roles.FirstOrDefaultAsync(role => role.IsSystem, cancellationToken);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken)
    {
        var normalized = Role.Normalize(name);
        return context.Roles.AnyAsync(role => role.NormalizedName == normalized, cancellationToken);
    }

    public void Add(Role role) => context.Roles.Add(role);

    public void Remove(Role role) => context.Roles.Remove(role);
}
