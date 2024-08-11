using Auth.Domain.Profiles;
using Domain.Core.Errors;
using Domain.Core.Events.DomainEvents;
using Shared.Domain;
using Shared.Domain.Abstractions;
using Shared.Domain.Result;
using Shared.Domain.Utility;

namespace Auth.Domain.Users;

public class User : Entity, ISoftDeletableEntity, IAuditableEntity
{
    private User(FirstName firstName, LastName lastName, Email email, byte[] passwordHash, byte[] passwordSalt) : base(Guid.NewGuid())
    {
        Ensure.NotNull(firstName, "The first name is required.", nameof(firstName));
        Ensure.NotNull(lastName, "The last name is required.", nameof(lastName));
        Ensure.NotNull(email, "The email is required.", nameof(email));

        FirstName = firstName;
        LastName = lastName;
        Email = email;
        PasswordHash = passwordHash;
        PasswordSalt = passwordSalt;
    }
    private User()
    {
    }
    public DateTime CreatedOnUtc { get; }
    public DateTime? ModifiedOnUtc { get; }
    public DateTime? DeletedOnUtc { get; }
    public bool Deleted { get; }
    public Guid? PaymentId { get; private set; }
    public Guid? SubscriptionId { get; private set; }
    public FirstName FirstName { get; private set; }
    public LastName LastName { get; private set; }
    public string FullName => $"{FirstName.Value} {LastName.Value}";
    public Email Email { get; private set; }
    public byte[] PasswordSalt { get; private set; }
    public byte[] PasswordHash { get; private set; }
    public ICollection<Profile>? Profiles { get; private set; }

    public static User Create(FirstName firstName, LastName lastName, Email email, byte[] passwordHash, byte[] passwordSalt)
    {
        var user = new User(firstName, lastName, email, passwordHash, passwordSalt);

        user.RaiseDomainEvent(new UserCreatedDomainEvent(user));

        return user;
    }

    public Result ChangePassword(string password, byte[] passwordHash)
    {
        if (passwordHash.SequenceEqual(PasswordHash))
        {
            return Result.Failure(DomainErrors.User.CannotChangePassword);
        }
        PasswordHash = passwordHash;

        RaiseDomainEvent(new UserPasswordChangedDomainEvent(this));
        return Result.Success();
    }
}
