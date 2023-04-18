using MovieAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace MovieAPI.Services
{
    public interface IMovieService
    {
        Task<IDataResult<List<Movie>>> GetAllAsync();
        Task<IDataResult<Movie>> GetByIdAsync(Guid id);
        Task<IResult> AddAsync(MovieDto movieDto);
        Task<IResult> UpdateAsync(Movie movie);
        Task<IResult> DeleteAsync(Movie movie);

    }

}
