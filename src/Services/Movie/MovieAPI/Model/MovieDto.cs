namespace MovieAPI.Model
{
    public class MovieDto
    {
        public string? MovieDescription { get; set; }
        public string? Title { get; set; }
        public int ReleaseYear { get; set; }
        public MpaaRatingType MpaaRating { get; set; }
        public ICollection<Genre>? Genre { get; set; }
    }
}
