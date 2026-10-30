namespace Auth.Domain.Roles;

/// <summary>Loads and stores roles for commands.</summary>
public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken);

    void Add(Role role);

    /// <summary>Deletes the role; users who had it lose it with the same save.</summary>
    void Remove(Role role);
}
