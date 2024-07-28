using Domain.Core.Abstractions;
using Domain.Core.Primitives;
using Domain.Core.Utility;

namespace Domain.Entities
{
    public class OperationClaim: Entity, IAuditableEntity, ISoftDeletableEntity
    {
        public OperationClaim(string name): base(Guid.NewGuid())
        {
            Ensure.NotEmpty(name, "The name is required.", nameof(Name));

            Name = name;
            CreatedOnUtc = DateTime.UtcNow;
            Deleted = false;
        }

        private OperationClaim()
        {
        }
        public DateTime CreatedOnUtc { get; }
        public DateTime? ModifiedOnUtc { get; }
        public DateTime? DeletedOnUtc { get; }
        public bool Deleted { get; }
        public string Name { get; set; }
        public ICollection<User>? Users { get; set; }
    }
}
