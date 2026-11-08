using Shared.Domain;

namespace Catalog.Domain.Content;

/// <summary>
/// What movies and series have in common: their description, genres, credits and whether customers
/// can see them. Deletion is soft: watch history will refer to these ids after the content is gone.
/// </summary>
public abstract class ContentItem : AggregateRoot, IAuditableEntity, ISoftDeletableEntity
{
    private readonly List<ContentGenre> _genres = [];
    private readonly List<Credit> _credits = [];
    private readonly List<ContentTranslation> _translations = [];

    protected ContentItem(Guid id, ContentInfo info)
        : base(id)
    {
        Info = info;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    protected ContentItem()
    {
    }

    public ContentInfo Info { get; private set; } = null!;

    public PublicationStatus Status { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>
    /// Grows with every change to the aggregate, its genres, credits, seasons and episodes included.
    /// An editor sends back the version they loaded, so that they cannot overwrite someone else's
    /// change without seeing it.
    /// </summary>
    public int Version { get; private set; }

    public bool IsPublished => Status == PublicationStatus.Published;

    /// <summary>Name of the stored poster; published content needs one.</summary>
    public string? PosterImage { get; private set; }

    public string? BackdropImage { get; private set; }

    public IReadOnlyCollection<ContentTranslation> Translations => _translations.AsReadOnly();

    public IReadOnlyCollection<ContentGenre> Genres => _genres.AsReadOnly();

    public IReadOnlyCollection<Credit> Credits => _credits.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void Describe(ContentInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Info = info;
        Version++;
    }

    /// <summary>Replaces the texts; the default language must be among them.</summary>
    public Result SetTexts(IReadOnlyDictionary<string, ContentText> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var languages = ContentText.CheckLanguages(texts.Keys);
        if (languages.IsFailure)
        {
            return languages;
        }

        ContentTranslation.Apply(_translations, texts);
        Version++;
        return Result.Success();
    }

    /// <summary>The text in the language, or in the default language when it has not been translated.</summary>
    public ContentTranslation TextFor(string language) => ContentTranslation.Pick(_translations, language);

    /// <summary>Replaces the genres. Published content cannot be left without one.</summary>
    public Result SetGenres(IEnumerable<Guid> genreIds)
    {
        ArgumentNullException.ThrowIfNull(genreIds);

        var wanted = genreIds.Distinct().ToList();
        if (wanted.Count == 0 && IsPublished)
        {
            return ContentErrors.GenreRequired;
        }

        _genres.RemoveAll(genre => !wanted.Contains(genre.GenreId));
        foreach (var genreId in wanted.Where(id => !_genres.Exists(genre => genre.GenreId == id)))
        {
            _genres.Add(new ContentGenre(genreId));
        }

        Version++;
        return Result.Success();
    }

    /// <summary>Replaces the credits; a person may hold several roles, but each role once.</summary>
    public Result SetCredits(IReadOnlyCollection<CreditInput> credits)
    {
        ArgumentNullException.ThrowIfNull(credits);

        if (credits.DistinctBy(credit => (credit.PersonId, credit.Role)).Count() != credits.Count)
        {
            return ContentErrors.CreditDuplicate;
        }

        if (credits.Any(credit => !Enum.IsDefined(credit.Role)))
        {
            return ContentErrors.CreditRoleInvalid;
        }

        var names = credits.Select(credit => string.IsNullOrWhiteSpace(credit.CharacterName) ? null : credit.CharacterName.Trim()).ToList();
        if (names.Any(name => name?.Length > Credit.CharacterNameMaxLength))
        {
            return ContentErrors.CharacterNameTooLong;
        }

        _credits.RemoveAll(existing => !credits.Any(credit => credit.PersonId == existing.PersonId && credit.Role == existing.Role));

        foreach (var (credit, name) in credits.Zip(names))
        {
            var existing = _credits.Find(item => item.PersonId == credit.PersonId && item.Role == credit.Role);
            if (existing is null)
            {
                _credits.Add(new Credit(credit.PersonId, credit.Role, name, credit.Order));
            }
            else
            {
                existing.Update(name, credit.Order);
            }
        }

        Version++;
        return Result.Success();
    }

    /// <summary>The name of the image in that slot.</summary>
    public string? ImageOf(ContentImageSlot slot) => slot == ContentImageSlot.Poster ? PosterImage : BackdropImage;

    public void SetImage(ContentImageSlot slot, string imageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);
        Store(slot, imageName);
    }

    /// <summary>Empties the slot. Published content must keep its poster.</summary>
    public Result RemoveImage(ContentImageSlot slot)
    {
        if (slot == ContentImageSlot.Poster && IsPublished)
        {
            return ContentErrors.PosterRequired;
        }

        Store(slot, null);
        return Result.Success();
    }

    /// <summary>Makes the content visible to customers; publishing it again changes nothing.</summary>
    public Result Publish(DateTimeOffset now)
    {
        if (IsPublished)
        {
            return Result.Success();
        }

        if (_genres.Count == 0)
        {
            return ContentErrors.GenreRequired;
        }

        var ready = CheckReadyToPublish();
        if (ready.IsFailure)
        {
            return ready;
        }

        if (PosterImage is null)
        {
            return ContentErrors.PosterRequired;
        }

        Status = PublicationStatus.Published;
        PublishedAt = now;
        Version++;
        return Result.Success();
    }

    public void Unpublish()
    {
        if (IsPublished)
        {
            Status = PublicationStatus.Draft;
            PublishedAt = null;
            Version++;
        }
    }

    /// <summary>Fails when the editor's copy is older than what is stored.</summary>
    public Result CheckVersion(int version) => version == Version ? Result.Success() : ContentErrors.VersionConflict;

    /// <summary>Whether the content may be deleted; the repository's removal is then a soft delete.</summary>
    public Result CanDelete() => IsPublished ? ContentErrors.MustBeUnpublished : Result.Success();

    private void Store(ContentImageSlot slot, string? imageName)
    {
        if (slot == ContentImageSlot.Poster)
        {
            PosterImage = imageName;
        }
        else
        {
            BackdropImage = imageName;
        }

        Version++;
    }

    /// <summary>For changes a derived aggregate makes to its own parts.</summary>
    protected void MarkChanged() => Version++;

    /// <summary>Rules of the specific kind of content, on top of the shared ones.</summary>
    protected virtual Result CheckReadyToPublish() => Result.Success();
}
