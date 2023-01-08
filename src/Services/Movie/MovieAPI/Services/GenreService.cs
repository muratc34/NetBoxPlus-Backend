using MovieAPI.Infrastructure.Repositories;
using MovieAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace MovieAPI.Services
{
    public class GenreService : IGenreService
    {
        private readonly IGenreRepository _genreRepository;

        public GenreService(IGenreRepository genreRepository)
        {
            _genreRepository = genreRepository;
        }

        public async Task<IResult> Add(Genre genre)
        {
            genre.Id = Guid.NewGuid();

            await _genreRepository.CreateAsync(genre);
            return new SuccessResult();
        }

        public async Task<IResult> Delete(Genre genre)
        {
            await _genreRepository.RemoveAsync(genre);
            return new SuccessResult();
        }

        public async Task<IDataResult<List<Genre>>> GetAll()
        {
            return new SuccessDataResult<List<Genre>>(await _genreRepository.GetAllAsync());
        }

        public async Task<IDataResult<Genre>> GetById(Guid id)
        {
            return new SuccessDataResult<Genre>(await _genreRepository.GetAsync(b => b.Id == id));
        }

        public async Task<IDataResult<List<Genre>>> GetByGenreCode(int code)
        {
            return new SuccessDataResult<List<Genre>>(await _genreRepository.GetAllAsync(g => g.GenreCode == code));
        }

        public async Task<IResult> Update(Genre genre)
        {
            await _genreRepository.UpdateAsync(genre);
            return new SuccessResult();
        }
    }

}
