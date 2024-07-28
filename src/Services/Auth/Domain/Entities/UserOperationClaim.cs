using Domain.Core.Primitives;
using Domain.Core.Utility;

namespace Domain.Entities
{
    public class UserOperationClaim : Entity
    {
        public UserOperationClaim() : base(Guid.NewGuid())
        {
            Ensure.NotEmpty(UserId, "The user identifier is required", nameof(UserId));
            Ensure.NotEmpty(OperationClaimId, "The operation claim identifier is required", nameof(OperationClaimId));
        }
        public Guid UserId { get; set; }
        public Guid OperationClaimId { get; set; }
        public User? User { get; set; }
        public OperationClaim? OperationClaim { get; set; }
    }
}
