using Shared.Domain.Abstractions;
using Shared.Domain.Utility;
using Shared.Domain;

namespace Auth.Domain.Profiles;

public class Profile : Entity, IAuditableEntity, ISoftDeletableEntity
{
    public Profile(string profileName) : base(Guid.NewGuid())
    {
        Ensure.NotEmpty(profileName, "The first name is required.", nameof(profileName));

        ProfileName = profileName;
    }

    private Profile()
    {
    }

    public DateTime CreatedOnUtc { get; }
    public DateTime? ModifiedOnUtc { get; }
    public DateTime? DeletedOnUtc { get; }
    public bool Deleted { get; }
    public Guid UserId { get; private set; }
    public string ProfileName { get; private set; }
    public byte[]? PinHash { get; private set; }
    public byte[]? PinSalt { get; private set; }
}
