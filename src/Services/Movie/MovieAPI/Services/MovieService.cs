using MovieAPI.Helper;
using MovieAPI.Infrastructure.Repositories;
using MovieAPI.Model;
using Shared.Results;
using System.Collections.Generic;
using System.Text.Json;
using IResult = Shared.Results.IResult;

namespace MovieAPI.Services
{
    public class MovieService : IMovieService
    {
        private readonly IMovieRepository _movieRepository;

        public MovieService(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository;
        }

        public async Task<IResult> AddAsync(MovieDto movieDto)
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
                AgeRating = movieDto.AgeRating,
                Genres = movieDto.Genre,
                PosterPath = newPosterPath,
                BackdropPicPath = newBackdropPicPath,
                TrailerPath = newTrailerPath,
                MovieClickCount = 0
            };

            await _movieRepository.CreateAsync(movie);

            return new SuccessResult();
        }

        public async Task<IResult> DeleteAsync(Movie movie)
        {
            await _movieRepository.RemoveAsync(movie);
            return new SuccessResult();
        }

        public async Task<IDataResult<List<Movie>>> GetAllAsync()
        {
            return new SuccessDataResult<List<Movie>>(await _movieRepository.GetAllIncludeAsync());
        }

        public async Task<IDataResult<Movie>> GetByIdAsync(Guid id)
        {
            return new SuccessDataResult<Movie>(await _movieRepository.GetIncludeData(m => m.Id == id));
        }

        public async Task<IResult> UpdateAsync(Movie movie)
        {
            await _movieRepository.UpdateAsync(movie);
            return new SuccessResult();
        }
    }

}
