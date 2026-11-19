using Catalog.Application.Abstractions;
using Catalog.Domain.Content;
using Catalog.Domain.Genres;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Application.Content;

/// <summary>Finds the names behind the genre and person ids of loaded content, in one query each.</summary>
internal sealed class ContentReader(ICatalogReadModel readModel, IImageStorage storage)
{
    public async Task<ContentNames> LoadNamesAsync(IEnumerable<ContentItem> items, CancellationToken cancellationToken)
    {
        var loaded = items.ToList();

        var genreIds = loaded.SelectMany(item => item.Genres).Select(genre => genre.GenreId).Distinct().ToList();
        var genres = await readModel.Genres
            .Where(genre => genreIds.Contains(genre.Id))
            .ToDictionaryAsync(genre => genre.Id, cancellationToken)
            .ConfigureAwait(false);

        var personIds = loaded.SelectMany(item => item.Credits).Select(credit => credit.PersonId).Distinct().ToList();
        var people = await readModel.People
            .Where(person => personIds.Contains(person.Id))
            .ToDictionaryAsync(person => person.Id, person => person.Name, cancellationToken)
            .ConfigureAwait(false);

        return new ContentNames(genres, people, storage);
    }
}

internal sealed record ContentNames(IReadOnlyDictionary<Guid, Genre> Genres, IReadOnlyDictionary<Guid, string> People, IImageStorage Storage)
{
    public Uri? ImageAddress(string? name) => name is null ? null : Storage.PublicAddress(name);

    public IReadOnlyList<GenreReference> GenresOf(ContentItem item, string language = Languages.Default)
        => [.. item.Genres.Select(genre => new GenreReference(genre.GenreId, Genres[genre.GenreId].NameFor(language))).OrderBy(genre => genre.Name, StringComparer.Ordinal)];

    public IReadOnlyList<CreditResponse> CreditsOf(ContentItem item)
        => [.. item.Credits
            .OrderBy(credit => credit.Order)
            .Select(credit => new CreditResponse(credit.PersonId, People[credit.PersonId], credit.Role, credit.CharacterName, credit.Order))];
}
