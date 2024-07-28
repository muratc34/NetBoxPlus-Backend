using Domain.Core.Abstractions;
using Domain.Core.Primitives;
using Domain.Core.Utility;

namespace Domain.Entities
{
    public class User : Entity, ISoftDeletableEntity, IAuditableEntity
    {
        private User(string firstName, string lastName, string email) :base(Guid.NewGuid())
        {
            Ensure.NotEmpty(firstName, "The first name is required.", nameof(firstName));
            Ensure.NotEmpty(lastName, "The last name is required.", nameof(lastName));
            Ensure.NotEmpty(email, "The email is required.", nameof(email));

            CreatedOnUtc = DateTime.UtcNow;
            Deleted = false;
            FirstName = firstName;
            LastName = lastName;
            Email = email;
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
        public string FirstName { get; private set; }
        public string LastName { get; private set; }
        public string FullName => $"{FirstName} {LastName}";
        public string Email { get; private set; }
        public byte[]? PasswordSalt { get; private set; }
        public byte[]? PasswordHash { get; private set; }
        public bool Status { get; private set; }
        public ICollection<OperationClaim>? OperationClaims { get; private set; }
        public ICollection<Profile>? Profiles { get; private set; }
    }
}
