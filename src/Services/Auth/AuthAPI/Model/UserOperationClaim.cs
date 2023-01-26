using Shared;
using System.ComponentModel.DataAnnotations;

namespace AuthAPI.Model
{
    public class UserOperationClaim : IEntity
    {
        [Key]
        [Required]
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid OperationClaimId { get; set; }
        public User? User { get; set; }
        public OperationClaim? OperationClaim { get; set; }
        
    }
}
