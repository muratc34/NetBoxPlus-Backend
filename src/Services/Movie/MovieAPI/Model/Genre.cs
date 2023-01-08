using Shared;
using System.ComponentModel.DataAnnotations;

namespace MovieAPI.Model
{
    public class Genre : IEntity
    {
        [Key]
        [Required]
        public Guid Id { get; set; }
        public int GenreCode { get; set; }
        public string? GenreTitle { get; set; }
    }
}
