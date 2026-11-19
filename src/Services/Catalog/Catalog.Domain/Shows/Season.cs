using Catalog.Domain.Content;
using Shared.Domain;

namespace Catalog.Domain.Shows;

/// <summary>A season's optional title in one language. Part of the <see cref="Series"/> aggregate.</summary>
public sealed class SeasonTranslation
{
    internal SeasonTranslation(string language, string title)
    {
        Language = language;
        Title = title;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private SeasonTranslation()
    {
    }

    public string Language { get; private set; } = string.Empty;

    public string Title { get; internal set; } = string.Empty;
}

/// <summary>Groups the episodes of a series. Part of the <see cref="Series"/> aggregate; only episodes are published.</summary>
public sealed class Season : Entity
{
    public const int MaxNumber = 1000;
    public const int TitleMaxLength = 200;

    private readonly List<Episode> _episodes = [];
    private readonly List<SeasonTranslation> _translations = [];

    internal Season(Guid id, int number, IReadOnlyDictionary<string, string> titles)
        : base(id)
    {
        Number = number;
        ApplyTitles(titles);
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Season()
    {
    }

    public int Number { get; private set; }

    public IReadOnlyCollection<SeasonTranslation> Translations => _translations.AsReadOnly();

    public IReadOnlyCollection<Episode> Episodes => _episodes.AsReadOnly();

    /// <summary>The title in the language, or in the default language; a season may have none.</summary>
    public string? TitleFor(string language)
        => (_translations.Find(translation => translation.Language == language)
            ?? _translations.Find(translation => translation.Language == Languages.Default))?.Title;

    internal bool HasEpisodeNumber(int number) => _episodes.Exists(episode => episode.Number == number);

    internal void Change(int number, IReadOnlyDictionary<string, string> titles)
    {
        Number = number;
        ApplyTitles(titles);
    }

    internal void Add(Episode episode) => _episodes.Add(episode);

    internal void Remove(Episode episode) => _episodes.Remove(episode);

    /// <summary>Blank titles are left out: a season need not have a title in any language.</summary>
    internal static Result<IReadOnlyDictionary<string, string>> CheckTitles(IReadOnlyDictionary<string, string?>? titles)
    {
        var given = titles ?? new Dictionary<string, string?>();
        var languages = ContentText.CheckLanguages(given.Keys, defaultRequired: false);
        if (languages.IsFailure)
        {
            return languages.Error;
        }

        var kept = given.Where(title => !string.IsNullOrWhiteSpace(title.Value)).ToDictionary(title => title.Key, title => title.Value!.Trim());
        return kept.Values.Any(title => title.Length > TitleMaxLength)
            ? SeriesErrors.SeasonTitleTooLong
            : Result.Success<IReadOnlyDictionary<string, string>>(kept);
    }

    internal static Result CheckNumber(int number)
        => number is >= 1 and <= MaxNumber ? Result.Success() : SeriesErrors.SeasonNumberOutOfRange;

    private void ApplyTitles(IReadOnlyDictionary<string, string> titles)
    {
        _translations.RemoveAll(translation => !titles.ContainsKey(translation.Language));

        foreach (var (language, title) in titles)
        {
            var existing = _translations.Find(translation => translation.Language == language);
            if (existing is null)
            {
                _translations.Add(new SeasonTranslation(language, title));
            }
            else
            {
                existing.Title = title;
            }
        }
    }
}
