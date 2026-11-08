using Catalog.Domain.Genres;
using Catalog.Domain.Movies;
using Catalog.Domain.People;
using Catalog.Domain.Shows;

namespace Catalog.Application.Abstractions;

/// <summary>Untracked queryable views for query handlers, which project straight to response types.</summary>
public interface ICatalogReadModel
{
    IQueryable<Movie> Movies { get; }

    IQueryable<Series> Series { get; }

    IQueryable<Person> People { get; }

    IQueryable<Genre> Genres { get; }
}
