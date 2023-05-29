using Shared;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuthAPI.Model
{
    public class User : IEntity
    {
        [Key]
        [Required]
        public Guid Id { get; set; }
        public Guid PaymentId { get; set; }
        public Guid? SubscriptionId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public byte[]? PasswordSalt { get; set; }
        public byte[]? PasswordHash { get; set; }
        public bool Status { get; set; }
        public ICollection<OperationClaim>? OperationClaims { get; set; }
        public ICollection<Profile>? Profiles { get; set; }
    }
}
