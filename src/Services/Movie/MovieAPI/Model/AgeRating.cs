using Shared;

namespace MovieAPI.Model
{
    public class AgeRating : IEntity
    {
        public Guid Id { get; set; }
        public Guid MovieId { get; set; }
        public int RatingCode { get; set; }
        public string? Rating { get; set; }

        // RatingCode = 1 --> +7
        // RatingCode = 2 --> +10
        // RatingCode = 3 --> +13
        // RatingCode = 4 --> +16
        // RatingCode = 5 --> +18

    }
}
