using Catalog.Domain.Content;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Content;

/// <summary>A title and synopsis in one language, as an editor enters them.</summary>
public sealed record ContentTextFields(string? Title, string? Synopsis)
{
    /// <summary>The checked texts, or the first problem with them.</summary>
    internal static Result<IReadOnlyDictionary<string, ContentText>> ToTexts(IReadOnlyDictionary<string, ContentTextFields> translations)
    {
        var texts = new Dictionary<string, ContentText>(StringComparer.Ordinal);
        foreach (var (language, fields) in translations)
        {
            var text = ContentText.Create(fields.Title, fields.Synopsis);
            if (text.IsFailure)
            {
                return text.Error;
            }

            texts[language] = text.Value;
        }

        return texts;
    }
}

/// <summary>What an editor fills in for a movie or series; saving replaces all of it.</summary>
public sealed record ContentFields(
    IReadOnlyDictionary<string, ContentTextFields> Translations,
    string? OriginalTitle,
    int ReleaseYear,
    AgeRating AgeRating,
    IReadOnlyList<Guid> GenreIds,
    IReadOnlyList<CreditInput> Credits)
{
    internal ContentInfo ToInfo() => ContentInfo.Create(OriginalTitle, ReleaseYear, AgeRating).Value;

    internal IReadOnlyDictionary<string, ContentText> ToTexts() => ContentTextFields.ToTexts(Translations).Value;

    internal IEnumerable<ValidationFailure> Validate()
    {
        var languages = ContentText.CheckLanguages(Translations.Keys);
        if (languages.IsFailure)
        {
            yield return new ValidationFailure("translations", languages.Error.Code, languages.Error.Message);
        }

        foreach (var (language, fields) in Translations)
        {
            foreach (var (field, error) in ContentText.Check(fields.Title, fields.Synopsis))
            {
                yield return new ValidationFailure($"translations.{language}.{field}", error.Code, error.Message);
            }
        }

        foreach (var (field, error) in ContentInfo.Check(OriginalTitle, ReleaseYear, AgeRating))
        {
            yield return new ValidationFailure(field, error.Code, error.Message);
        }
    }
}

public sealed record GenreReference(Guid Id, string Name);

public sealed record CreditResponse(Guid PersonId, string PersonName, CreditRole Role, string? CharacterName, int Order);

/// <summary>The check every customer-facing query makes on the language it is asked for.</summary>
internal static class LanguageValidation
{
    public static IEnumerable<ValidationFailure> Validate(string language)
    {
        if (!Languages.IsSupported(language))
        {
            yield return new ValidationFailure("lang", ContentErrors.UnsupportedLanguage.Code, ContentErrors.UnsupportedLanguage.Message);
        }
    }
}
