using Catalog.Domain.Content;
using Shared.Domain;

namespace Catalog.Domain.Genres;

/// <summary>A genre's name in one language. Part of the <see cref="Genre"/> aggregate.</summary>
public sealed class GenreTranslation
{
    internal GenreTranslation(string language, string name)
    {
        Language = language;
        Name = name;
        NormalizedName = Genre.Normalize(language, name);
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private GenreTranslation()
    {
    }

    public string Language { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>The name in upper case by the rules of its language; unique within the language.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    internal void Rename(string name)
    {
        Name = name;
        NormalizedName = Genre.Normalize(Language, name);
    }
}

/// <summary>A genre, unique by name in each language whatever the case. Cannot be deleted while content uses it.</summary>
public sealed class Genre : AggregateRoot, IAuditableEntity
{
    public const int NameMaxLength = 64;

    private readonly List<GenreTranslation> _translations = [];

    private Genre(Guid id)
        : base(id)
    {
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Genre()
    {
    }

    public IReadOnlyCollection<GenreTranslation> Translations => _translations.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public static Result<Genre> Create(IReadOnlyDictionary<string, string?> names)
    {
        var genre = new Genre(Guid.CreateVersion7());
        var renamed = genre.Rename(names);
        return renamed.IsFailure ? renamed.Error : genre;
    }

    /// <summary>The name in the language, or in the default language when it has not been translated.</summary>
    public string NameFor(string language)
        => (_translations.Find(translation => translation.Language == language)
            ?? _translations.First(translation => translation.Language == Languages.Default)).Name;

    public Result Rename(IReadOnlyDictionary<string, string?> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var kept = names.Where(name => !string.IsNullOrWhiteSpace(name.Value)).ToDictionary(name => name.Key, name => name.Value!.Trim());
        var languages = ContentText.CheckLanguages(names.Keys, defaultRequired: false);
        if (languages.IsFailure)
        {
            return languages;
        }

        if (!kept.ContainsKey(Languages.Default))
        {
            return GenreErrors.NameRequired;
        }

        if (kept.Values.Any(name => name.Length > NameMaxLength))
        {
            return GenreErrors.NameTooLong;
        }

        _translations.RemoveAll(translation => !kept.ContainsKey(translation.Language));
        foreach (var (language, name) in kept)
        {
            var existing = _translations.Find(translation => translation.Language == language);
            if (existing is null)
            {
                _translations.Add(new GenreTranslation(language, name));
            }
            else
            {
                existing.Rename(name);
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Upper case by the rules of the language. For Turkish, i and ı are handled first so that "ilk"
    /// and "İlk" are the same name; written out rather than using the tr-TR culture, which the
    /// chiseled image has no data for.
    /// </summary>
    public static string Normalize(string language, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var trimmed = name.Trim();
        return language == Languages.Turkish
            ? trimmed.Replace('i', 'İ').Replace('ı', 'I').ToUpperInvariant()
            : trimmed.ToUpperInvariant();
    }
}
