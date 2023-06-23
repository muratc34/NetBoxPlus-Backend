using Shared;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieAPI.Model
{
    public class Genre : IEntity
    {
        public Guid Id { get; set; }
        public int GenreCode { get; set; }
        public string? GenreTitle { get; set; }
    }
}
