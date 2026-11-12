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

    public static readonly Error LastProfile = Error.Conflict("Profile.LastProfile", "The last profile of an account cannot be removed.");
}

public sealed record ProfileCreatedDomainEvent(Guid UserId, Guid ProfileId, AgeRating MaxAgeRating, int Revision) : IDomainEvent;

public sealed record ProfileChangedDomainEvent(Guid UserId, Guid ProfileId, AgeRating MaxAgeRating, int Revision) : IDomainEvent;

public sealed record ProfileRemovedDomainEvent(Guid UserId, Guid ProfileId, int Revision) : IDomainEvent;
