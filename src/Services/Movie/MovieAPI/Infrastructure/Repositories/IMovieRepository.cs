using MovieAPI.Model;
using Shared;
using System.Linq.Expressions;

namespace MovieAPI.Infrastructure.Repositories
{
    public interface IMovieRepository : IRepository<Movie>
    {
        Task<Movie> GetIncludeData(Expression<Func<Movie, bool>> filter);
        Task<List<Movie>> GetAllIncludeAsync(Expression<Func<Movie, bool>> filter = null!);
    }
}
