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

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
}
