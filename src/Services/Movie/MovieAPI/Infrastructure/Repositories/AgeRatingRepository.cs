using MovieAPI.Model;
using Shared.Repository;

namespace MovieAPI.Infrastructure.Repositories
{
    public class AgeRatingRepository : RepositoryBase<AgeRating, MovieContext>, IAgeRatingRepository
    {
    }
}
