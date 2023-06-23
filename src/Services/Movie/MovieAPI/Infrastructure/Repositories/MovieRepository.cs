using Microsoft.EntityFrameworkCore;
using MovieAPI.Model;
using Nest;
using Shared.Repository;
using System.Linq.Expressions;

namespace MovieAPI.Infrastructure.Repositories
{
#nullable disable
    public class MovieRepository : RepositoryBase<Movie, MovieContext>, IMovieRepository
    {
        public async Task<MovieDetailDto> GetDetailDataAsync(Guid id)
        {
            using (MovieContext context = new MovieContext())
            {
                var movieData = await context.Movies.SingleOrDefaultAsync(m => m.Id == id);

                if (movieData != null)
                {
                    movieData.MovieClickCount++;
                    await context.SaveChangesAsync(); 
                }

                var result = from movie in context.Movies
                             join ageRating in context.AgeRatings
                             on movie.AgeRating.Id equals ageRating.Id
                             where movie.Id == id
                             select new MovieDetailDto
                             {
                                 MovieId = movie.Id,
                                 Title = movie.Title,
                                 MovieDescription = movie.MovieDescription,
                                 PosterPath = movie.PosterPath,
                                 BackdropPicPath = movie.BackdropPicPath,
                                 TrailerPath = movie.TrailerPath,
                                 MoviePath = movie.MoviePath,
                                 MovieClickCount = movie.MovieClickCount,
                                 ReleaseYear = movie.ReleaseYear,
                                 AgeRating = ageRating,
                                 Genres = context.MovieGenres.Where(mg => mg.MovieId == movie.Id)
                                            .Select(mg => context.Genres.FirstOrDefault(g => g.Id == mg.GenreId))
                                            .ToList()
                             };

                return await result.SingleOrDefaultAsync(); ;
            }
        }

        public async Task<List<MovieDetailDto>> GetAllDetailAsync()
        {
            using (MovieContext context = new MovieContext())
            {
                var result = from movie in context.Movies
                             join ageRating in context.AgeRatings
                             on movie.AgeRating.Id equals ageRating.Id
                             select new MovieDetailDto
                             {
                                 MovieId = movie.Id,
                                 Title = movie.Title,
                                 MovieDescription = movie.MovieDescription,
                                 PosterPath = movie.PosterPath,
                                 BackdropPicPath = movie.BackdropPicPath,
                                 TrailerPath = movie.TrailerPath,
                                 MoviePath = movie.MoviePath,
                                 MovieClickCount = movie.MovieClickCount,
                                 ReleaseYear = movie.ReleaseYear,
                                 AgeRating = ageRating,
                                 Genres = context.MovieGenres.Where(mg => mg.MovieId == movie.Id)
                                            .Select(mg => context.Genres.FirstOrDefault(g => g.Id == mg.GenreId))
                                            .ToList()
                             };

                return await result.ToListAsync();
            }
        }

        public async Task<List<MovieDetailDto>> GetAllDetailByGenreIdAsync(Guid? genreId)
        {
            using (MovieContext context = new MovieContext())
            {
                var result = from movie in context.Movies
                             join ageRating in context.AgeRatings 
                             on movie.AgeRating.Id equals ageRating.Id
                             join movieGenre in context.MovieGenres 
                             on movie.Id equals movieGenre.MovieId
                             where genreId == Guid.Empty || movieGenre.GenreId == genreId
                             select new MovieDetailDto
                             {
                                 MovieId = movie.Id,
                                 Title = movie.Title,
                                 MovieDescription = movie.MovieDescription,
                                 PosterPath = movie.PosterPath,
                                 BackdropPicPath = movie.BackdropPicPath,
                                 TrailerPath = movie.TrailerPath,
                                 MoviePath = movie.MoviePath,
                                 MovieClickCount = movie.MovieClickCount,
                                 ReleaseYear = movie.ReleaseYear,
                                 AgeRating = ageRating,
                                 Genres =  context.MovieGenres.Where(mg => mg.MovieId == movie.Id)
                                            .Select(mg => context.Genres.FirstOrDefault(g => g.Id == mg.GenreId))
                                            .ToList()
                             };

                return await result.GroupBy(m => m.MovieId).Select(g => g.First()).ToListAsync();
            }
        }

        public async Task<List<MovieDetailDto>> GetByGenreSimilarityAsync(List<Guid> genreIds)
        {
            using(MovieContext context = new MovieContext())
            {
                var baseQuery = from movie in context.Movies
                                join ageRating in context.AgeRatings on movie.AgeRating.Id equals ageRating.Id
                                join movieGenre in context.MovieGenres on movie.Id equals movieGenre.MovieId
                                where genreIds.Contains(movieGenre.GenreId)
                                select new
                                {
                                    Movie = movie,
                                    AgeRating = ageRating,
                                    GenreIdCount = context.MovieGenres
                                        .Count(mg => mg.MovieId == movie.Id && genreIds.Contains(mg.GenreId))
                                };

                var groupedMovies = await baseQuery.GroupBy(item => item.Movie)
                                                  .Select(group => new
                                                  {
                                                      Movie = group.Key,
                                                      AgeRating = group.First().AgeRating,
                                                      GenreIdCount = group.First().GenreIdCount
                                                  })
                                                  .ToListAsync();

                var result = groupedMovies.OrderByDescending(item => item.GenreIdCount)
                                          .Take(11)
                                          .Select(item => new MovieDetailDto
                                          {
                                              MovieId = item.Movie.Id,
                                              Title = item.Movie.Title,
                                              MovieDescription = item.Movie.MovieDescription,
                                              PosterPath = item.Movie.PosterPath,
                                              BackdropPicPath = item.Movie.BackdropPicPath,
                                              TrailerPath = item.Movie.TrailerPath,
                                              MoviePath = item.Movie.MoviePath,
                                              MovieClickCount = item.Movie.MovieClickCount,
                                              ReleaseYear = item.Movie.ReleaseYear,
                                              AgeRating = item.AgeRating,
                                              Genres = context.MovieGenres
                                                  .Where(mg => mg.MovieId == item.Movie.Id)
                                                  .Select(mg => context.Genres.FirstOrDefault(g => g.Id == mg.GenreId))
                                                  .ToList()
                                          })
                                          .ToList();

                return result;
            }
        }

    }
}
