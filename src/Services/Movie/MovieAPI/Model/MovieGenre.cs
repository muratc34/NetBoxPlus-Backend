namespace MovieAPI.Model
{
    public class MovieGenre
    {
        public Guid Id { get; set; }
        public Guid MovieId { get; set; }
        public Guid GenreId { get; set; }
    }
}
