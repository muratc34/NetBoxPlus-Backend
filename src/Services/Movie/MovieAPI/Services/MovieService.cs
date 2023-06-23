using MovieAPI.Helper;
using MovieAPI.Infrastructure.Repositories;
using MovieAPI.Model;
using Nest;
using Shared.Results;
using System.Collections.Generic;
using System.Text.Json;
using IResult = Shared.Results.IResult;

namespace MovieAPI.Services
{
    public class MovieService : IMovieService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly IElasticClient _elasticClient;

        public MovieService(IMovieRepository movieRepository, IElasticClient elasticClient)
        {
            _movieRepository = movieRepository;
            _elasticClient = elasticClient;
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
                //AgeRating = movieDto.AgeRating,
                PosterPath = newPosterPath,
                BackdropPicPath = newBackdropPicPath,
                TrailerPath = newTrailerPath,
                MovieClickCount = 0
            };

            await _movieRepository.CreateAsync(movie);
            await _elasticClient.IndexAsync(movie, idx => idx.Index("movie"));

            return new SuccessResult();
        }

        public async Task<IResult> DeleteAsync(Movie movie)
        {
            await _movieRepository.RemoveAsync(movie);
            return new SuccessResult();
        }

        public async Task<IDataResult<List<MovieDetailDto>>> GetAllAsync()
        {
            return new SuccessDataResult<List<MovieDetailDto>>(await _movieRepository.GetAllDetailAsync());
        }

        public async Task<IDataResult<List<MovieDetailDto>>> GetByGenreIdAsync(Guid? genreId)
        {
            if (genreId == null)
            {
                await _movieRepository.GetAllAsync();
            }
            return new SuccessDataResult<List<MovieDetailDto>>(await _movieRepository.GetAllDetailByGenreIdAsync(genreId));
        }

        public async Task<IDataResult<MovieDetailDto>> GetByIdAsync(Guid id)
        {
            return new SuccessDataResult<MovieDetailDto>(await _movieRepository.GetDetailDataAsync(id));
        }

        public async Task<IDataResult<List<Movie>>> GetBySearch(string keyword)
        {
            if(keyword != null)
            {
                var result = await _elasticClient.SearchAsync<Movie>(s => s
                .From(0)
                .Size(100)
                .Query(q => q
                    .Wildcard(t => t
                        .Field(f => f.Title)
                        .Value($"*{keyword.ToLower()}*")
                        )
                    )
                );

                return new SuccessDataResult<List<Movie>>(result.Documents.ToList());
            }

            return new SuccessDataResult<List<Movie>>();
        }

        public async Task<IDataResult<List<MovieDetailDto>>> GetByGenreSimilatiry(List<Guid> genreIds)
        {
            return new SuccessDataResult<List<MovieDetailDto>>(await _movieRepository.GetByGenreSimilarityAsync(genreIds));
        }

        public async Task<IResult> UpdateAsync(Movie movie)
        {
            await _movieRepository.UpdateAsync(movie);
            return new SuccessResult();
        }
    }
}
