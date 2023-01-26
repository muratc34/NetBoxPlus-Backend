using Shared;
using System.ComponentModel.DataAnnotations;

namespace AuthAPI.Model
{
    public class OperationClaim : IEntity
    {
        [Key]
        [Required]
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public ICollection<User>? Users { get; set; }
    }
}
