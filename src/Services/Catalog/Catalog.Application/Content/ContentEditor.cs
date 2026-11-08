using Catalog.Application.Abstractions;
using Catalog.Domain.Content;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;

namespace Catalog.Application.Content;

/// <summary>The steps creating and changing a movie or series have in common.</summary>
internal sealed class ContentEditor(ICatalogReadModel readModel)
{
    /// <summary>Whether every genre and person the fields name exists.</summary>
    public async Task<Result> CheckReferencesAsync(ContentFields fields, CancellationToken cancellationToken)
    {
        var genreIds = fields.GenreIds.Distinct().ToList();
        var genres = await readModel.Genres.CountAsync(genre => genreIds.Contains(genre.Id), cancellationToken).ConfigureAwait(false);
        if (genres != genreIds.Count)
        {
            return ContentErrors.UnknownGenre;
        }

        var personIds = fields.Credits.Select(credit => credit.PersonId).Distinct().ToList();
        var people = await readModel.People.CountAsync(person => personIds.Contains(person.Id), cancellationToken).ConfigureAwait(false);
        return people == personIds.Count ? Result.Success() : ContentErrors.UnknownPerson;
    }

    public static Result ApplyLists(ContentItem item, ContentFields fields)
    {
        var genres = item.SetGenres(fields.GenreIds);
        return genres.IsFailure ? genres : item.SetCredits(fields.Credits);
    }
}
