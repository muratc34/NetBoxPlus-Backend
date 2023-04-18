namespace MovieAPI.Model
{
    public class MovieDto
    {
        public string? MovieDescription { get; set; }
        public string? Title { get; set; }
        public int ReleaseYear { get; set; }
        public AgeRating? AgeRating { get; set; }
        public ICollection<Genre>? Genre { get; set; }
        public IFormFile? Poster { get; set; }
        public IFormFile? Backdrop { get; set; }
        public IFormFile? Trailer { get; set; }

    }
}
