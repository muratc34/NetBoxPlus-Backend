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

    public const int MaxProfiles = 5;

    /// <summary>The profile every account starts with.</summary>
    public const string FirstProfileName = "Main";

    private readonly List<UserRole> _roles = [];
    private readonly List<Profile> _profiles = [];

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

    /// <summary>When the user proved they own the address by following the e-mailed link; not required to use the service.</summary>
    public DateTimeOffset? EmailVerifiedAt { get; private set; }

    public bool IsEmailVerified => EmailVerifiedAt is not null;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    public IReadOnlyCollection<Profile> Profiles => _profiles.AsReadOnly();

    public static User Register(EmailAddress email, string passwordHash)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var user = new User(Guid.CreateVersion7(), email, passwordHash);
        user.RaiseDomainEvent(new UserRegisteredDomainEvent(user.Id, email.Value));
        user.AddProfile(FirstProfileName, AgeRating.Age18);
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

    /// <summary>
    /// Stores the same password hashed with newer parameters, after a successful sign-in. Not a
    /// password change: the user is not told.
    /// </summary>
    public void RehashPassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    /// <summary>A new password chosen by the user; the user is told by e-mail.</summary>
    public void ChangePassword(string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        RaiseDomainEvent(new PasswordChangedDomainEvent(Id, Guid.CreateVersion7(), now));
    }

    /// <summary>
    /// A new password set with an e-mailed reset link. Following the link proves the address, so it
    /// counts as verified, and it lifts a lockout as a successful sign-in does.
    /// </summary>
    public void ResetPassword(string passwordHash, DateTimeOffset now)
    {
        ChangePassword(passwordHash, now);
        RecordSuccessfulSignIn();
        VerifyEmail(now);
    }

    /// <summary>Records the verification, announced once; verifying again keeps the first time.</summary>
    public void VerifyEmail(DateTimeOffset now)
    {
        if (EmailVerifiedAt is not null)
        {
            return;
        }

        EmailVerifiedAt = now;
        RaiseDomainEvent(new EmailVerifiedDomainEvent(Id, Email.Value, now));
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

    public Result<Profile> AddProfile(string? name, AgeRating maxAgeRating)
    {
        var checkedName = Profile.CheckName(name);
        if (checkedName.IsFailure)
        {
            return checkedName.Error;
        }

        if (!Enum.IsDefined(maxAgeRating))
        {
            return ProfileErrors.AgeRatingInvalid;
        }

        if (_profiles.Count >= MaxProfiles)
        {
            return ProfileErrors.LimitReached;
        }

        if (IsNameTaken(checkedName.Value, exceptId: null))
        {
            return ProfileErrors.NameTaken;
        }

        var profile = Profile.Create(Id, checkedName.Value, maxAgeRating);
        _profiles.Add(profile);
        RaiseDomainEvent(new ProfileCreatedDomainEvent(Id, profile.Id, profile.MaxAgeRating, profile.Revision));
        return profile;
    }

    public Result ChangeProfile(Guid profileId, string? name, AgeRating maxAgeRating)
    {
        var profile = _profiles.Find(item => item.Id == profileId);
        if (profile is null)
        {
            return ProfileErrors.NotFound;
        }

        var checkedName = Profile.CheckName(name);
        if (checkedName.IsFailure)
        {
            return checkedName.Error;
        }

        if (!Enum.IsDefined(maxAgeRating))
        {
            return ProfileErrors.AgeRatingInvalid;
        }

        if (IsNameTaken(checkedName.Value, profileId))
        {
            return ProfileErrors.NameTaken;
        }

        if (profile.Change(checkedName.Value, maxAgeRating))
        {
            RaiseDomainEvent(new ProfileChangedDomainEvent(Id, profile.Id, profile.MaxAgeRating, profile.Revision));
        }

        return Result.Success();
    }

    public Result RemoveProfile(Guid profileId)
    {
        var profile = _profiles.Find(item => item.Id == profileId);
        if (profile is null)
        {
            return ProfileErrors.NotFound;
        }

        if (_profiles.Count == 1)
        {
            return ProfileErrors.LastProfile;
        }

        profile.MarkRemoved();
        _profiles.Remove(profile);
        RaiseDomainEvent(new ProfileRemovedDomainEvent(Id, profile.Id, profile.Revision));
        return Result.Success();
    }

    private bool IsNameTaken(string name, Guid? exceptId)
    {
        var normalized = Profile.Normalize(name);
        return _profiles.Exists(profile => profile.Id != exceptId && profile.NormalizedName == normalized);
    }
}

public sealed record UserRegisteredDomainEvent(Guid UserId, string Email) : IDomainEvent;

/// <param name="ChangeId">Identifies this change, so the notice is e-mailed once per change.</param>
public sealed record PasswordChangedDomainEvent(Guid UserId, Guid ChangeId, DateTimeOffset ChangedAt) : IDomainEvent;

/// <summary>The address was proven for the first time.</summary>
public sealed record EmailVerifiedDomainEvent(Guid UserId, string Email, DateTimeOffset VerifiedAt) : IDomainEvent;
