using MovieAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace MovieAPI.Services
{
    public interface IMovieService
    {
        Task<IDataResult<List<Movie>>> GetAll();
        Task<IDataResult<Movie>> GetById(Guid id);
        Task<IResult> Add(MovieDto movieDto);
        Task<IResult> Update(Movie movie);
        Task<IResult> Delete(Movie movie);

    }

}
