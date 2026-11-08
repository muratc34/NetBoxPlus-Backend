using Catalog.Domain.Genres;
using Catalog.Domain.Movies;
using Catalog.Domain.People;
using Catalog.Domain.Shows;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Persistence;

internal sealed class MovieRepository(CatalogDbContext context) : IMovieRepository
{
    public Task<Movie?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Movies.FirstOrDefaultAsync(movie => movie.Id == id, cancellationToken);

    public void Add(Movie movie) => context.Movies.Add(movie);

    public void Remove(Movie movie) => context.Movies.Remove(movie);
}

internal sealed class SeriesRepository(CatalogDbContext context) : ISeriesRepository
{
    // Split: seasons, episodes, genres and credits in one join would multiply each other's rows.
    public Task<Series?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Series
            .Include(series => series.Seasons)
            .ThenInclude(season => season.Episodes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(series => series.Id == id, cancellationToken);

    public void Add(Series series) => context.Series.Add(series);

    public void Remove(Series series) => context.Series.Remove(series);
}

internal sealed class PersonRepository(CatalogDbContext context) : IPersonRepository
{
    public Task<Person?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.People.FirstOrDefaultAsync(person => person.Id == id, cancellationToken);

    public void Add(Person person) => context.People.Add(person);

    public void Remove(Person person) => context.People.Remove(person);
}

internal sealed class GenreRepository(CatalogDbContext context) : IGenreRepository
{
    public Task<Genre?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Genres.FirstOrDefaultAsync(genre => genre.Id == id, cancellationToken);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken)
    {
        var normalized = Genre.Normalize(name);
        return context.Genres.AnyAsync(genre => genre.NormalizedName == normalized, cancellationToken);
    }

    public void Add(Genre genre) => context.Genres.Add(genre);

    public void Remove(Genre genre) => context.Genres.Remove(genre);
}
