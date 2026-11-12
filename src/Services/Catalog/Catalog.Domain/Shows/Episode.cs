using Catalog.Domain.Content;
using Shared.Domain;

namespace Catalog.Domain.Shows;

/// <summary>
/// One episode. Part of the <see cref="Series"/> aggregate. Episodes are published one by one, so a
/// running series can gain an episode that stays hidden until it is ready.
/// </summary>
public sealed class Episode : Entity
{
    public const int MaxNumber = 10000;

    private readonly List<ContentTranslation> _translations = [];

    internal Episode(Guid id, EpisodeDetails details)
        : base(id)
    {
        Change(details);
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Episode()
    {
    }

    public int Number { get; private set; }

    public int RuntimeMinutes { get; private set; }

    public PublicationStatus Status { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public bool IsPublished => Status == PublicationStatus.Published;

    public IReadOnlyCollection<ContentTranslation> Translations => _translations.AsReadOnly();

    public ContentVideo Video { get; private set; } = new();

    /// <summary>The text in the language, or in the default language when it has not been translated.</summary>
    public ContentTranslation TextFor(string language) => ContentTranslation.Pick(_translations, language);

    internal void Change(EpisodeDetails details)
    {
        Number = details.Number;
        RuntimeMinutes = details.RuntimeMinutes;
        ContentTranslation.Apply(_translations, details.Texts);
    }

    internal void Publish(DateTimeOffset now)
    {
        if (!IsPublished)
        {
            Status = PublicationStatus.Published;
            PublishedAt = now;
        }
    }

    internal void Unpublish()
    {
        Status = PublicationStatus.Draft;
        PublishedAt = null;
    }
}

/// <summary>The editable fields of an episode, checked.</summary>
public sealed record EpisodeDetails
{
    private EpisodeDetails(int number, IReadOnlyDictionary<string, ContentText> texts, int runtimeMinutes)
    {
        Number = number;
        Texts = texts;
        RuntimeMinutes = runtimeMinutes;
    }

    public int Number { get; }

    public IReadOnlyDictionary<string, ContentText> Texts { get; }

    public int RuntimeMinutes { get; }

    public static Result<EpisodeDetails> Create(int number, IReadOnlyDictionary<string, ContentText> texts, int runtimeMinutes)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (number is < 1 or > Episode.MaxNumber)
        {
            return SeriesErrors.EpisodeNumberOutOfRange;
        }

        var languages = ContentText.CheckLanguages(texts.Keys);
        if (languages.IsFailure)
        {
            return languages.Error;
        }

        var runtime = Runtime.Check(runtimeMinutes);
        return runtime.IsFailure ? runtime.Error : new EpisodeDetails(number, texts, runtimeMinutes);
    }
}
