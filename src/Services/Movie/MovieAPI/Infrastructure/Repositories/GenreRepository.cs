using MovieAPI.Model;
using Shared.Repository;

namespace MovieAPI.Infrastructure.Repositories
{
    public class GenreRepository : RepositoryBase<Genre, MovieContext>, IGenreRepository
    {

    }
}
