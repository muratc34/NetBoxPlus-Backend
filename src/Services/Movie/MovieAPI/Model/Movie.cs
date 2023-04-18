using Shared;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieAPI.Model
{
    public class Movie : IEntity
    {
        [Key]
        [Required]
        public Guid Id { get; set; }
        public string? MovieDescription { get; set; }
        public string? Title { get; set; }
        public string? PosterPath { get; set; }
        public string? BackdropPicPath { get; set; }
        public string? MoviePath { get; set; }
        public string? TrailerPath { get; set; }
        public int MovieClickCount { get; set; }
        public int ReleaseYear { get; set; }
        public AgeRating? AgeRating { get; set; }
        public ICollection<Genre>? Genres { get; set; }
    }
}
