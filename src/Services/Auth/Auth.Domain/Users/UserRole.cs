namespace Auth.Domain.Users;

/// <summary>A role given to a user. Part of the <see cref="User"/> aggregate; the role itself is its own aggregate.</summary>
public sealed class UserRole
{
    internal UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private UserRole()
    {
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }
}
