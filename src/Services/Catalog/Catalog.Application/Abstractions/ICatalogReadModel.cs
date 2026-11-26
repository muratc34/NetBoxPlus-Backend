using Catalog.Domain.Genres;
using Catalog.Domain.Movies;
using Catalog.Domain.People;
using Catalog.Domain.Popularity;
using Catalog.Domain.Shows;

namespace Catalog.Application.Abstractions;

/// <summary>Untracked queryable views for query handlers, which project straight to response types.</summary>
public interface ICatalogReadModel
{
    IQueryable<Movie> Movies { get; }

    IQueryable<Series> Series { get; }

    IQueryable<Person> People { get; }

    IQueryable<Genre> Genres { get; }

    /// <summary>The scores of the titles watched most in the last week; a title not in it scores 0.</summary>
    IQueryable<TitlePopularity> TitlePopularity { get; }

    /// <summary>Movies whose title or original title contains the text, ignoring case.</summary>
    IQueryable<Movie> MoviesTitled(string fragment);

    /// <summary>Series whose title or original title contains the text, ignoring case.</summary>
    IQueryable<Series> SeriesTitled(string fragment);

    /// <summary>The series with their seasons and episodes loaded.</summary>
    IQueryable<Series> WithSeasons(IQueryable<Series> series);

    /// <summary>People whose name contains the text, ignoring case.</summary>
    IQueryable<Person> PeopleNamed(string fragment);
}
