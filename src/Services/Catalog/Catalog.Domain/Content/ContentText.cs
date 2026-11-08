using Shared.Domain;

namespace Catalog.Domain.Content;

/// <summary>A title and synopsis in one language, checked.</summary>
public sealed record ContentText
{
    public const int TitleMaxLength = 200;
    public const int SynopsisMaxLength = 2000;

    private ContentText(string title, string synopsis)
    {
        Title = title;
        Synopsis = synopsis;
    }

    public string Title { get; }

    public string Synopsis { get; }

    public static Result<ContentText> Create(string? title, string? synopsis)
    {
        var problem = Check(title, synopsis).FirstOrDefault();
        return problem.Error is not null ? problem.Error : new ContentText(title!.Trim(), synopsis!.Trim());
    }

    /// <summary>Every problem with the values, with the field it belongs to.</summary>
    public static IEnumerable<(string Field, Error Error)> Check(string? title, string? synopsis)
    {
        var trimmedTitle = title?.Trim();
        if (string.IsNullOrEmpty(trimmedTitle))
        {
            yield return ("title", ContentErrors.TitleRequired);
        }
        else if (trimmedTitle.Length > TitleMaxLength)
        {
            yield return ("title", ContentErrors.TitleTooLong);
        }

        var trimmedSynopsis = synopsis?.Trim();
        if (string.IsNullOrEmpty(trimmedSynopsis))
        {
            yield return ("synopsis", ContentErrors.SynopsisRequired);
        }
        else if (trimmedSynopsis.Length > SynopsisMaxLength)
        {
            yield return ("synopsis", ContentErrors.SynopsisTooLong);
        }
    }

    /// <summary>Whether a set of languages is usable: all supported, the default one included.</summary>
    public static Result CheckLanguages(IEnumerable<string> languages, bool defaultRequired = true)
    {
        var list = languages.ToList();
        if (list.Exists(language => !Languages.IsSupported(language)))
        {
            return ContentErrors.UnsupportedLanguage;
        }

        return defaultRequired && !list.Contains(Languages.Default) ? ContentErrors.DefaultLanguageRequired : Result.Success();
    }
}

/// <summary>The title and synopsis of a movie, series or episode in one language. Part of that aggregate.</summary>
public sealed class ContentTranslation
{
    private ContentTranslation(string language, ContentText text)
    {
        Language = language;
        Title = text.Title;
        Synopsis = text.Synopsis;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private ContentTranslation()
    {
    }

    public string Language { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Synopsis { get; private set; } = string.Empty;

    /// <summary>Makes the list hold exactly these languages, keeping the entries that stay.</summary>
    internal static void Apply(List<ContentTranslation> translations, IReadOnlyDictionary<string, ContentText> texts)
    {
        translations.RemoveAll(translation => !texts.ContainsKey(translation.Language));

        foreach (var (language, text) in texts)
        {
            var existing = translations.Find(translation => translation.Language == language);
            if (existing is null)
            {
                translations.Add(new ContentTranslation(language, text));
            }
            else
            {
                existing.Title = text.Title;
                existing.Synopsis = text.Synopsis;
            }
        }
    }

    /// <summary>The text in the language, or in the default language when it has not been translated.</summary>
    internal static ContentTranslation Pick(IEnumerable<ContentTranslation> translations, string language)
    {
        var list = translations as IReadOnlyList<ContentTranslation> ?? [.. translations];
        return list.FirstOrDefault(translation => translation.Language == language)
            ?? list.First(translation => translation.Language == Languages.Default);
    }
}
