using Catalog.Domain.Content;
using Shared.Domain;

namespace Catalog.Domain.People;

/// <summary>A person changed; content crediting them has to follow.</summary>
public sealed record PersonChangedDomainEvent(Guid PersonId) : IDomainEvent;

/// <summary>A person's biography in one language. Part of the <see cref="Person"/> aggregate.</summary>
public sealed class PersonTranslation
{
    internal PersonTranslation(string language, string biography)
    {
        Language = language;
        Biography = biography;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private PersonTranslation()
    {
    }

    public string Language { get; private set; } = string.Empty;

    public string Biography { get; internal set; } = string.Empty;
}

/// <summary>Someone credited on movies or series. Cannot be deleted while credited.</summary>
public sealed class Person : AggregateRoot, IAuditableEntity
{
    public const int NameMaxLength = 200;
    public const int BiographyMaxLength = 4000;

    private readonly List<PersonTranslation> _translations = [];

    private Person(Guid id, string name)
        : base(id)
    {
        Name = name;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Person()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public IReadOnlyCollection<PersonTranslation> Translations => _translations.AsReadOnly();

    /// <summary>Name of the stored photo, when there is one.</summary>
    public string? PhotoImage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public static Result<Person> Create(string? name, IReadOnlyDictionary<string, string?>? biographies)
    {
        var person = new Person(Guid.CreateVersion7(), string.Empty);
        var changed = person.Change(name, biographies);
        return changed.IsFailure ? changed.Error : person;
    }

    /// <summary>The biography in the language or the default one; it can be missing in both.</summary>
    public PersonTranslation? BiographyFor(string language)
        => _translations.Find(translation => translation.Language == language)
            ?? _translations.Find(translation => translation.Language == Languages.Default);

    public Result Change(string? name, IReadOnlyDictionary<string, string?>? biographies)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            return PersonErrors.NameRequired;
        }

        if (trimmedName.Length > NameMaxLength)
        {
            return PersonErrors.NameTooLong;
        }

        var given = biographies ?? new Dictionary<string, string?>();
        var languages = ContentText.CheckLanguages(given.Keys, defaultRequired: false);
        if (languages.IsFailure)
        {
            return languages;
        }

        var kept = given.Where(item => !string.IsNullOrWhiteSpace(item.Value)).ToDictionary(item => item.Key, item => item.Value!.Trim());
        if (kept.Values.Any(biography => biography.Length > BiographyMaxLength))
        {
            return PersonErrors.BiographyTooLong;
        }

        Name = trimmedName;
        _translations.RemoveAll(translation => !kept.ContainsKey(translation.Language));
        foreach (var (language, biography) in kept)
        {
            var existing = _translations.Find(translation => translation.Language == language);
            if (existing is null)
            {
                _translations.Add(new PersonTranslation(language, biography));
            }
            else
            {
                existing.Biography = biography;
            }
        }

        RaiseDomainEvent(new PersonChangedDomainEvent(Id));
        return Result.Success();
    }

    public void SetPhoto(string? imageName) => PhotoImage = imageName;
}
