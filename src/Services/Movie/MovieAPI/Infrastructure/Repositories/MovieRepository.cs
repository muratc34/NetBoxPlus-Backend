﻿using Microsoft.EntityFrameworkCore;
using MovieAPI.Model;
using Shared.Repository;
using System.Linq.Expressions;

namespace MovieAPI.Infrastructure.Repositories
{
#nullable disable
    public class MovieRepository : RepositoryBase<Movie, MovieContext>, IMovieRepository
    {
        public async Task<Movie> GetIncludeData(Expression<Func<Movie, bool>> filter)
        {
            using (MovieContext context = new MovieContext())
            {
                var movie = await context.Set<Movie>().Include("Genres").Include("AgeRating").SingleOrDefaultAsync(filter);
                movie.MovieClickCount += 1;
                await context.SaveChangesAsync();
                return movie;
            }
        }

        public async Task<List<Movie>> GetAllIncludeAsync(Expression<Func<Movie, bool>> filter = null!)
        {
            using (MovieContext context = new MovieContext())
            {
                return await (filter == null ? context.Set<Movie>().Include("Genres").Include("AgeRating").ToListAsync() : context.Set<Movie>().Include("Genres").Where(filter).ToListAsync());
            }
        }
    }
}
