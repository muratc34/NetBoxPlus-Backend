using MovieAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace MovieAPI.Services
{
    public interface IMovieService
    {
        Task<IDataResult<List<MovieDetailDto>>> GetAllAsync();
        Task<IDataResult<MovieDetailDto>> GetByIdAsync(Guid id);
        Task<IDataResult<List<MovieDetailDto>>> GetByGenreIdAsync(Guid? genreId);
        Task<IDataResult<List<MovieDetailDto>>> GetByGenreSimilatiry(List<Guid> genreIds);
        Task<IDataResult<List<Movie>>> GetBySearch(string keyword);
        Task<IResult> AddAsync(MovieDto movieDto);
        Task<IResult> UpdateAsync(Movie movie);
        Task<IResult> DeleteAsync(Movie movie);

    }

}
