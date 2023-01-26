using Shared;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieAPI.Model
{
    public class Genre : IEntity
    {
        [Key]
        [Required]
        public Guid Id { get; set; }
        public Guid MovieId { get; set; }
        public int GenreCode { get; set; }
        public string? GenreTitle { get; set; }
    }
}
