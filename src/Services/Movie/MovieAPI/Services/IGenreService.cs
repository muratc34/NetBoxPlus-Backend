using MovieAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace MovieAPI.Services
{
    public interface IGenreService
    {
        Task<IDataResult<List<Genre>>> GetAllAsync();
        Task<IDataResult<Genre>> GetByIdAsync(Guid id);
        Task<IDataResult<List<Genre>>> GetByGenreCodeAsync(int code);
        Task<IResult> AddAsync(Genre genre);
        Task<IResult> UpdateAsync(Genre genre);
        Task<IResult> DeleteAsync(Genre genre);
    }

}
