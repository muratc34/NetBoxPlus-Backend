using MovieAPI.Helper;
using MovieAPI.Infrastructure.Repositories;
using MovieAPI.Model;
using Shared.Results;
using IResult = Shared.Results.IResult;

namespace MovieAPI.Services
{
    public class MovieService : IMovieService
    {
        private readonly IMovieRepository _movieRepository;
        //private readonly IPublishEndpoint _publishEndpoint;

        public MovieService(IMovieRepository movieRepository/*, IPublishEndpoint publishEndpoint*/)
        {
            _movieRepository = movieRepository;
            //_publishEndpoint = publishEndpoint;
        }

        public async Task<IResult> Add(MovieDto movieDto)
        {
            var newPosterPath = FileHelper.Add(movieDto.Poster!, @"/images/");
            var newBackdropPicPath = FileHelper.Add(movieDto.Backdrop!, @"/backdrops/");
            var newTrailerPath = FileHelper.Add(movieDto.Trailer!, @"/trailers/");

            var movie = new Movie
            {
                Id = Guid.NewGuid(),
                MovieDescription = movieDto.MovieDescription,
                Title = movieDto.Title,
                ReleaseYear = movieDto.ReleaseYear,
                MpaaRating = movieDto.MpaaRating,
                Genres = movieDto.Genre,
                PosterPath = newPosterPath,
                BackdropPicPath = newBackdropPicPath,
                TrailerPath = newTrailerPath,
                MovieClickCount = 0
            };

            //var genre = new GenreCreatedEvent
            //{
            //    Id = movie.Genre!.GenreId,
            //    GenreCode = movie.Genre.GenreCode,
            //    Title = movie.Genre.GenreTitle
            //};
            //movie.ImagePath = FileHelper.Add(file,@"/images/");

            await _movieRepository.CreateAsync(movie);
            //await _publishEndpoint.Publish(genre);

            return new SuccessResult();
        }

        public async Task<IResult> Delete(Movie movie)
        {
            await _movieRepository.RemoveAsync(movie);
            return new SuccessResult();
        }

        public async Task<IDataResult<List<Movie>>> GetAll()
        {
            return new SuccessDataResult<List<Movie>>(await _movieRepository.GetAllIncludeAsync());
        }

        public async Task<IDataResult<Movie>> GetById(Guid id)
        {
            return new SuccessDataResult<Movie>(await _movieRepository.GetIncludeData(m => m.Id == id));
        }

        public async Task<IResult> Update(Movie movie)
        {
            await _movieRepository.UpdateAsync(movie);
            return new SuccessResult();
        }
    }

}
