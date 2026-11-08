using Catalog.Application.Abstractions;
using Catalog.Domain.Content;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Search;

/// <summary>Builds index documents from the titles as they are now.</summary>
internal sealed class SearchDocumentBuilder(CatalogDbContext context)
{
    /// <summary>The documents of the ids that are published; any other id is not in the result.</summary>
    public async Task<IReadOnlyList<SearchDocument>> BuildAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var movies = await context.Movies.AsNoTracking()
            .Where(movie => ids.Contains(movie.Id) && movie.Status == PublicationStatus.Published)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var series = await context.Series.AsNoTracking()
            .Where(item => ids.Contains(item.Id) && item.Status == PublicationStatus.Published)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = movies.Cast<ContentItem>().Concat(series).ToList();

        var genreIds = items.SelectMany(item => item.Genres).Select(genre => genre.GenreId).Distinct().ToList();
        var genres = await context.Genres.AsNoTracking()
            .Where(genre => genreIds.Contains(genre.Id))
            .ToDictionaryAsync(genre => genre.Id, cancellationToken)
            .ConfigureAwait(false);

        var personIds = items.SelectMany(item => item.Credits).Select(credit => credit.PersonId).Distinct().ToList();
        var people = await context.People.AsNoTracking()
            .Where(person => personIds.Contains(person.Id))
            .ToDictionaryAsync(person => person.Id, person => person.Name, cancellationToken)
            .ConfigureAwait(false);

        return [.. items.Select(item =>
        {
            var titles = item.Translations.ToDictionary(text => text.Language, text => text.Title);
            var list = item.Genres
                .Select(genre => new SearchDocumentGenre(genre.GenreId, genres[genre.GenreId].Translations.ToDictionary(name => name.Language, name => name.Name)))
                .ToList();

            return new SearchDocument(
                item.Id,
                item.Kind,
                titles,
                item.Translations.ToDictionary(text => text.Language, text => text.Synopsis),
                titles,
                item.Info.OriginalTitle,
                item.Info.ReleaseYear,
                item.Info.AgeRating,
                item.PosterImage,
                item.PublishedAt,
                [.. item.Genres.Select(genre => genre.GenreId)],
                list.SelectMany(genre => genre.Names)
                    .GroupBy(name => name.Key, name => name.Value)
                    .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)[.. group]),
                list,
                [.. item.Credits.Select(credit => people[credit.PersonId]).Distinct()]);
        })];
    }
}
