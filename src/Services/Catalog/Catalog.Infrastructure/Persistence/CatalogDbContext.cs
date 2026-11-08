using Catalog.Application.Abstractions;
using Catalog.Domain.Genres;
using Catalog.Domain.Movies;
using Catalog.Domain.People;
using Catalog.Domain.Shows;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence;

namespace Catalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : ServiceDbContext(options), ICatalogReadModel
{
    /// <summary>Configuration key under <c>ConnectionStrings</c>; the same name in every service.</summary>
    public const string ConnectionStringName = "Database";

    public DbSet<Movie> Movies => Set<Movie>();

    public DbSet<Series> Series => Set<Series>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<Genre> Genres => Set<Genre>();

    IQueryable<Movie> ICatalogReadModel.Movies => Movies.AsNoTracking();

    IQueryable<Series> ICatalogReadModel.Series => Series.AsNoTracking();

    IQueryable<Person> ICatalogReadModel.People => People.AsNoTracking();

    IQueryable<Genre> ICatalogReadModel.Genres => Genres.AsNoTracking();

    IQueryable<Movie> ICatalogReadModel.MoviesIncludingDeleted => Movies.IgnoreQueryFilters().AsNoTracking();

    IQueryable<Series> ICatalogReadModel.SeriesIncludingDeleted => Series.IgnoreQueryFilters().AsNoTracking();

    IQueryable<Movie> ICatalogReadModel.MoviesTitled(string fragment)
    {
        var pattern = Pattern(fragment);
        return Movies.AsNoTracking().Where(movie => movie.Translations.Any(translation => EF.Functions.ILike(translation.Title, pattern, "!"))
            || (movie.Info.OriginalTitle != null && EF.Functions.ILike(movie.Info.OriginalTitle, pattern, "!")));
    }

    IQueryable<Series> ICatalogReadModel.SeriesTitled(string fragment)
    {
        var pattern = Pattern(fragment);
        return Series.AsNoTracking().Where(series => series.Translations.Any(translation => EF.Functions.ILike(translation.Title, pattern, "!"))
            || (series.Info.OriginalTitle != null && EF.Functions.ILike(series.Info.OriginalTitle, pattern, "!")));
    }

    // Split: seasons, episodes, genres and credits in one join would multiply each other's rows.
    IQueryable<Series> ICatalogReadModel.WithSeasons(IQueryable<Series> series)
        => series.Include(item => item.Seasons).ThenInclude(season => season.Episodes).AsSplitQuery();

    IQueryable<Person> ICatalogReadModel.PeopleNamed(string fragment)
    {
        var pattern = Pattern(fragment);
        return People.AsNoTracking().Where(person => EF.Functions.ILike(person.Name, pattern, "!"));
    }

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);

    // "Contains", with the search text taken literally: % and _ are not wildcards.
    private static string Pattern(string fragment)
        => "%" + fragment.Trim().Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal) + "%";
}
