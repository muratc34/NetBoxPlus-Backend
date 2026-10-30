using Shared.Domain;

namespace Auth.Domain.Users;

/// <summary>
/// An account: its sign-in identity, password hash, lock state and roles. The password itself
/// never reaches the domain; hashing happens in the application layer.
/// </summary>
public sealed class User : AggregateRoot, IAuditableEntity
{
    /// <summary>Consecutive failed sign-ins that lock the account for <see cref="LockoutDuration"/>.</summary>
    public const int MaxFailedSignIns = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly List<UserRole> _roles = [];

    private User(Guid id, EmailAddress email, string passwordHash)
        : base(id)
    {
        Email = email;
        PasswordHash = passwordHash;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private User()
    {
    }

    public EmailAddress Email { get; private set; } = null!;

    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// Failed sign-ins since the last successful one or the last lockout. Incremented in the
    /// database (see <see cref="IUserRepository.RecordFailedSignInAsync"/>), not through this type,
    /// so that concurrent attempts cannot overwrite each other's count.
    /// </summary>
    public int FailedSignInCount { get; private set; }

    /// <summary>Set when the failed sign-ins reach <see cref="MaxFailedSignIns"/>.</summary>
    public DateTimeOffset? LockedOutUntil { get; private set; }

    /// <summary>Disabled by an administrator: no sign-in until enabled again.</summary>
    public bool IsDisabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    public static User Register(EmailAddress email, string passwordHash)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var user = new User(Guid.CreateVersion7(), email, passwordHash);
        user.RaiseDomainEvent(new UserRegisteredDomainEvent(user.Id, email.Value));
        return user;
    }

    public bool IsLockedOut(DateTimeOffset now) => LockedOutUntil > now;

    /// <summary>Whether the account may sign in at all, before the password is checked.</summary>
    public Result CanSignIn(DateTimeOffset now)
    {
        if (IsDisabled)
        {
            return UserErrors.Disabled;
        }

        return IsLockedOut(now) ? UserErrors.LockedOut : Result.Success();
    }

    public void RecordSuccessfulSignIn()
    {
        FailedSignInCount = 0;
        LockedOutUntil = null;
    }

    public void ChangePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public void Disable() => IsDisabled = true;

    public void Enable()
    {
        IsDisabled = false;
        FailedSignInCount = 0;
        LockedOutUntil = null;
    }

    /// <summary>Gives the user a role; giving a role the user already has changes nothing.</summary>
    public void AssignRole(Guid roleId)
    {
        Ensure.NotEmpty(roleId);

        if (!_roles.Exists(role => role.RoleId == roleId))
        {
            _roles.Add(new UserRole(Id, roleId));
        }
    }

    public void RemoveRole(Guid roleId) => _roles.RemoveAll(role => role.RoleId == roleId);
}

public sealed record UserRegisteredDomainEvent(Guid UserId, string Email) : IDomainEvent;
