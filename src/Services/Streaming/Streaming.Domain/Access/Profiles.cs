using Shared.Domain;

namespace Streaming.Domain.Access;

/// <summary>The youngest audience a title is meant for, and the oldest a profile may watch.</summary>
public enum AgeRating
{
    All,
    Age7,
    Age13,
    Age16,
    Age18,
}

/// <summary>
/// A viewer profile of the account service, kept from its events. A removed profile stays as a
/// marker, so that a late event about it cannot bring it back.
/// </summary>
public sealed class ProfileCopy : AggregateRoot
{
    private ProfileCopy(Guid id, Guid userId)
        : base(id)
    {
        UserId = userId;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private ProfileCopy()
    {
    }

    public Guid UserId { get; private set; }

    public AgeRating MaxAgeRating { get; private set; }

    public bool IsRemoved { get; private set; }

    /// <summary>The account service's revision of the state kept here; older news is ignored.</summary>
    public int Revision { get; private set; }

    public static ProfileCopy Create(Guid profileId, Guid userId) => new(profileId, userId) { Revision = -1 };

    public bool Apply(AgeRating maxAgeRating, int revision)
    {
        if (revision <= Revision)
        {
            return false;
        }

        MaxAgeRating = maxAgeRating;
        Revision = revision;
        return true;
    }

    public bool Remove(int revision)
    {
        if (revision <= Revision)
        {
            return false;
        }

        IsRemoved = true;
        Revision = revision;
        return true;
    }
}

public interface IProfileCopyRepository
{
    Task<ProfileCopy?> GetByIdAsync(Guid profileId, CancellationToken cancellationToken);

    void Add(ProfileCopy profile);
}
