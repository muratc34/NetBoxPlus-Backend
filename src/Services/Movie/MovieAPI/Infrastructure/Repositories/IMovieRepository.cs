using MovieAPI.Model;
using Shared;
using System.Linq.Expressions;

namespace MovieAPI.Infrastructure.Repositories
{
    public interface IMovieRepository : IRepository<Movie>
    {
        Task<MovieDetailDto> GetDetailDataAsync(Guid id);
        Task<List<MovieDetailDto>> GetAllDetailAsync();
        Task<List<MovieDetailDto>> GetAllDetailByGenreIdAsync(Guid? genreId);
        Task<List<MovieDetailDto>> GetByGenreSimilarityAsync(List<Guid> genreIds);
    }
}
