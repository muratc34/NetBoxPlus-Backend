using MovieAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace MovieAPI.Services
{
    public interface IGenreService
    {
        Task<IDataResult<List<Genre>>> GetAll();
        Task<IDataResult<Genre>> GetById(Guid id);
        Task<IDataResult<List<Genre>>> GetByGenreCode(int code);
        Task<IResult> Add(Genre genre);
        Task<IResult> Update(Genre genre);
        Task<IResult> Delete(Genre genre);
    }

}
