using Auth.Domain.Profiles.DomainEvents;
using Shared.Domain;
using Shared.Domain.Abstractions;
using Shared.Domain.Utility;

namespace Auth.Domain.Profiles;

public class Profile : Entity, IAuditableEntity, ISoftDeletableEntity
{
    public Profile(Guid userId, ProfileName profileName, byte[]? pinHash, byte[]? pinSalt) : base(Guid.NewGuid())
    {
        Ensure.NotNull(profileName, "The first name is required.", nameof(profileName));

        UserId = userId;
        ProfileName = profileName;
        PinHash = pinHash;
        PinSalt = pinSalt;
    }

    private Profile()
    {
    }

    public DateTime CreatedOnUtc { get; }
    public DateTime? ModifiedOnUtc { get; }
    public DateTime? DeletedOnUtc { get; }
    public bool Deleted { get; }
    public Guid UserId { get; private set; }
    public ProfileName ProfileName { get; private set; }
    public byte[]? PinHash { get; private set; }
    public byte[]? PinSalt { get; private set; }

    public static Profile Create(Guid userId, ProfileName profileName, byte[]? pinHash, byte[]? pinSalt)
    {
        var profile = new Profile(userId, profileName, pinHash, pinSalt);
        profile.RaiseDomainEvent(new ProfileCreatedDomainEvent(profile));
        return profile;
    }
}
