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

    public class MovieDetailDto
    {
        public Guid MovieId { get; set; }
        public string? Title { get; set; }
        public string? MovieDescription { get; set; }
        public string? PosterPath { get; set; }
        public string? BackdropPicPath { get; set; }
        public string? TrailerPath { get; set; }
        public string? MoviePath { get; set; }
        public int MovieClickCount { get; set; }
        public int ReleaseYear { get; set; }
        public AgeRating? AgeRating { get; set; }
        public ICollection<Genre>? Genres { get; set; }
    }
}
