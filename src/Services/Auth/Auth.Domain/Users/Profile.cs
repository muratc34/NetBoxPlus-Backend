using Shared.Domain;

namespace Auth.Domain.Users;

/// <summary>The oldest audience a profile may watch for; the same names as the catalog's age ratings.</summary>
public enum AgeRating
{
    All,
    Age7,
    Age13,
    Age16,
    Age18,
}

/// <summary>
/// One viewer of an account. Watching history belongs to a profile, and its age rating limits what
/// it may play. Part of the <see cref="User"/> aggregate.
/// </summary>
public sealed class Profile : Entity
{
    public const int NameMaxLength = 32;

    /// <summary>The digits of a PIN.</summary>
    public const int PinLength = 4;

    /// <summary>Consecutive wrong PINs that lock the profile for <see cref="PinLockoutDuration"/>.</summary>
    public const int MaxFailedPins = 5;

    public static readonly TimeSpan PinLockoutDuration = TimeSpan.FromMinutes(15);

    private Profile(Guid id, Guid userId, string name, AgeRating maxAgeRating)
        : base(id)
    {
        UserId = userId;
        Rename(name);
        MaxAgeRating = maxAgeRating;
        Revision = 1;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Profile()
    {
    }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>The name in upper case: names are unique in an account whatever the case.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    public AgeRating MaxAgeRating { get; private set; }

    /// <summary>The hashed PIN that must be entered to use the profile; none when the profile is open to the whole account.</summary>
    public string? PinHash { get; private set; }

    public bool IsLocked => PinHash is not null;

    /// <summary>
    /// Wrong PINs since the last right one or the last lockout. Incremented in the database (see
    /// <see cref="IUserRepository.RecordFailedPinAsync"/>), so that concurrent attempts each count.
    /// </summary>
    public int FailedPinCount { get; private set; }

    /// <summary>Set when the wrong PINs reach <see cref="MaxFailedPins"/>.</summary>
    public DateTimeOffset? PinLockedUntil { get; private set; }

    public bool IsPinLockedOut(DateTimeOffset now) => PinLockedUntil > now;

    /// <summary>Exactly <see cref="PinLength"/> digits 0–9.</summary>
    public static bool IsWellFormedPin(string? pin) => pin is { Length: PinLength } && pin.All(char.IsAsciiDigit);

    /// <summary>Goes up with every change; events carry it, so that consumers can tell old from new.</summary>
    public int Revision { get; private set; }

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();

    /// <summary>The trimmed name, or the error that makes it unusable.</summary>
    public static Result<string> CheckName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return ProfileErrors.NameRequired;
        }

        return trimmed.Length > NameMaxLength ? ProfileErrors.NameTooLong : trimmed;
    }

    internal static Profile Create(Guid userId, string name, AgeRating maxAgeRating)
        => new(Guid.CreateVersion7(), userId, name, maxAgeRating);

    /// <summary>Whether anything changed.</summary>
    internal bool Change(string name, AgeRating maxAgeRating)
    {
        if (Name == name && MaxAgeRating == maxAgeRating)
        {
            return false;
        }

        Rename(name);
        MaxAgeRating = maxAgeRating;
        Revision++;
        return true;
    }

    internal void MarkRemoved() => Revision++;

    /// <summary>Whether anything changed: a new PIN always does (a new hash), removing a missing one does not.</summary>
    internal bool ChangePin(string? pinHash)
    {
        if (pinHash is null && PinHash is null)
        {
            return false;
        }

        PinHash = pinHash;
        FailedPinCount = 0;
        PinLockedUntil = null;
        Revision++;
        return true;
    }

    internal void RecordRightPin()
    {
        FailedPinCount = 0;
        PinLockedUntil = null;
    }

    private void Rename(string name)
    {
        Name = name;
        NormalizedName = Normalize(name);
    }
}

public static class ProfileErrors
{
    public static readonly Error NotFound = Error.NotFound("Profile.NotFound", "The profile was not found.");

    public static readonly Error NameRequired = Error.Validation("Profile.NameRequired", "The profile name is required.");

    public static readonly Error NameTooLong = Error.Validation("Profile.NameTooLong", $"The profile name must be at most {Profile.NameMaxLength} characters.");

    public static readonly Error AgeRatingInvalid = Error.Validation("Profile.AgeRatingInvalid", "The age rating is not valid.");

    public static readonly Error NameTaken = Error.Conflict("Profile.NameTaken", "The account already has a profile with this name.");

    public static readonly Error LimitReached = Error.Conflict("Profile.LimitReached", $"An account can have at most {User.MaxProfiles} profiles.");

    public static readonly Error PinInvalid = Error.Validation("Profile.PinInvalid", $"The PIN must be {Profile.PinLength} digits.");

    public static readonly Error PinIncorrect = Error.Validation("Profile.PinIncorrect", "The PIN is not correct.");

    public static readonly Error SessionRequired = Error.Unauthorized("Profile.SessionRequired", "Sign in again to unlock the profile.");

    public static readonly Error PinLocked = Error.Conflict("Profile.PinLocked", "Too many wrong PINs; try again later.");

    public static readonly Error LastProfile = Error.Conflict("Profile.LastProfile", "The last profile of an account cannot be removed.");
}

public sealed record ProfileCreatedDomainEvent(Guid UserId, Guid ProfileId, AgeRating MaxAgeRating, int Revision) : IDomainEvent;

public sealed record ProfileChangedDomainEvent(Guid UserId, Guid ProfileId, AgeRating MaxAgeRating, int Revision, bool Locked) : IDomainEvent;

public sealed record ProfileRemovedDomainEvent(Guid UserId, Guid ProfileId, int Revision) : IDomainEvent;
