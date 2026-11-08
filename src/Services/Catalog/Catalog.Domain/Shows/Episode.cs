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
    public const int TitleMaxLength = 200;
    public const int SynopsisMaxLength = 2000;

    internal Episode(Guid id, EpisodeDetails details)
        : base(id)
    {
        Number = details.Number;
        Title = details.Title;
        Synopsis = details.Synopsis;
        RuntimeMinutes = details.RuntimeMinutes;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Episode()
    {
    }

    public int Number { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Synopsis { get; private set; } = string.Empty;

    public int RuntimeMinutes { get; private set; }

    public PublicationStatus Status { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public bool IsPublished => Status == PublicationStatus.Published;

    internal void Change(EpisodeDetails details)
    {
        Number = details.Number;
        Title = details.Title;
        Synopsis = details.Synopsis;
        RuntimeMinutes = details.RuntimeMinutes;
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
    private EpisodeDetails(int number, string title, string synopsis, int runtimeMinutes)
    {
        Number = number;
        Title = title;
        Synopsis = synopsis;
        RuntimeMinutes = runtimeMinutes;
    }

    public int Number { get; }

    public string Title { get; }

    public string Synopsis { get; }

    public int RuntimeMinutes { get; }

    public static Result<EpisodeDetails> Create(int number, string? title, string? synopsis, int runtimeMinutes)
    {
        if (number is < 1 or > Episode.MaxNumber)
        {
            return SeriesErrors.EpisodeNumberOutOfRange;
        }

        var trimmedTitle = title?.Trim();
        if (string.IsNullOrEmpty(trimmedTitle))
        {
            return SeriesErrors.EpisodeTitleRequired;
        }

        if (trimmedTitle.Length > Episode.TitleMaxLength)
        {
            return SeriesErrors.EpisodeTitleTooLong;
        }

        var trimmedSynopsis = synopsis?.Trim();
        if (string.IsNullOrEmpty(trimmedSynopsis))
        {
            return SeriesErrors.EpisodeSynopsisRequired;
        }

        if (trimmedSynopsis.Length > Episode.SynopsisMaxLength)
        {
            return SeriesErrors.EpisodeSynopsisTooLong;
        }

        var runtime = Runtime.Check(runtimeMinutes);
        return runtime.IsFailure ? runtime.Error : new EpisodeDetails(number, trimmedTitle, trimmedSynopsis, runtimeMinutes);
    }
}
