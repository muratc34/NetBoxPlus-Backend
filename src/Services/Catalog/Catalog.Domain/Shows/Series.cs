using Catalog.Domain.Content;
using Shared.Domain;

namespace Catalog.Domain.Shows;

/// <summary>A series with its seasons and episodes, which change only through this aggregate.</summary>
public sealed class Series : ContentItem
{
    private readonly List<Season> _seasons = [];

    private Series(Guid id, ContentInfo info)
        : base(id, info)
    {
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Series()
    {
    }

    public IReadOnlyCollection<Season> Seasons => _seasons.AsReadOnly();

    public override ContentKind Kind => ContentKind.Series;

    public static Result<Series> Create(ContentInfo info, IReadOnlyDictionary<string, ContentText> texts)
    {
        ArgumentNullException.ThrowIfNull(info);

        var series = new Series(Guid.CreateVersion7(), info);
        var set = series.SetTexts(texts);
        return set.IsFailure ? set.Error : series;
    }

    public Result<Season> AddSeason(int number, IReadOnlyDictionary<string, string?>? titles)
    {
        var checkedNumber = Season.CheckNumber(number);
        if (checkedNumber.IsFailure)
        {
            return checkedNumber.Error;
        }

        var checkedTitle = Season.CheckTitles(titles);
        if (checkedTitle.IsFailure)
        {
            return checkedTitle.Error;
        }

        if (_seasons.Exists(season => season.Number == number))
        {
            return SeriesErrors.SeasonNumberTaken;
        }

        var added = new Season(Guid.CreateVersion7(), number, checkedTitle.Value);
        _seasons.Add(added);
        MarkChanged();
        return added;
    }

    public Result ChangeSeason(Guid seasonId, int number, IReadOnlyDictionary<string, string?>? titles)
    {
        if (FindSeason(seasonId) is not { } season)
        {
            return SeriesErrors.SeasonNotFound;
        }

        var checkedNumber = Season.CheckNumber(number);
        if (checkedNumber.IsFailure)
        {
            return checkedNumber;
        }

        var checkedTitle = Season.CheckTitles(titles);
        if (checkedTitle.IsFailure)
        {
            return checkedTitle.Error;
        }

        if (_seasons.Exists(other => other.Id != seasonId && other.Number == number))
        {
            return SeriesErrors.SeasonNumberTaken;
        }

        season.Change(number, checkedTitle.Value);
        MarkChanged();
        return Result.Success();
    }

    public Result RemoveSeason(Guid seasonId)
    {
        if (FindSeason(seasonId) is not { } season)
        {
            return SeriesErrors.SeasonNotFound;
        }

        if (season.Episodes.Any(episode => episode.IsPublished))
        {
            return SeriesErrors.SeasonHasPublishedEpisodes;
        }

        _seasons.Remove(season);
        MarkChanged();
        return Result.Success();
    }

    public Result<Episode> AddEpisode(Guid seasonId, EpisodeDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (FindSeason(seasonId) is not { } season)
        {
            return SeriesErrors.SeasonNotFound;
        }

        if (season.HasEpisodeNumber(details.Number))
        {
            return SeriesErrors.EpisodeNumberTaken;
        }

        var episode = new Episode(Guid.CreateVersion7(), details);
        season.Add(episode);
        MarkChanged();
        return episode;
    }

    public Result ChangeEpisode(Guid episodeId, EpisodeDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (Find(episodeId) is not (var season, var episode))
        {
            return SeriesErrors.EpisodeNotFound;
        }

        if (season.Episodes.Any(other => other.Id != episodeId && other.Number == details.Number))
        {
            return SeriesErrors.EpisodeNumberTaken;
        }

        episode.Change(details);
        MarkChanged();
        return Result.Success();
    }

    public Result RemoveEpisode(Guid episodeId)
    {
        if (Find(episodeId) is not (var season, var episode))
        {
            return SeriesErrors.EpisodeNotFound;
        }

        if (episode.IsPublished)
        {
            return SeriesErrors.EpisodeIsPublished;
        }

        season.Remove(episode);
        MarkChanged();
        return Result.Success();
    }

    public Result PublishEpisode(Guid episodeId, DateTimeOffset now)
    {
        if (Find(episodeId) is not (_, var episode))
        {
            return SeriesErrors.EpisodeNotFound;
        }

        if (!episode.Video.IsPlayable)
        {
            return ContentErrors.VideoRequired;
        }

        episode.Publish(now);
        MarkChanged();
        return Result.Success();
    }

    /// <summary>A report from the streaming service on an episode's video; not an edit, the version stays.</summary>
    public Result ApplyEpisodeVideo(Guid episodeId, Guid videoId, VideoState state, int revision)
    {
        if (Find(episodeId) is not (_, var episode))
        {
            return SeriesErrors.EpisodeNotFound;
        }

        episode.Video.Apply(videoId, state, revision);
        return Result.Success();
    }

    /// <summary>Hides the episode; a published series must keep at least one.</summary>
    public Result UnpublishEpisode(Guid episodeId)
    {
        if (Find(episodeId) is not (_, var episode))
        {
            return SeriesErrors.EpisodeNotFound;
        }

        if (IsPublished && episode.IsPublished && PublishedEpisodes().Count() == 1)
        {
            return SeriesErrors.PublishedEpisodeRequired;
        }

        episode.Unpublish();
        MarkChanged();
        return Result.Success();
    }

    protected override Result CheckReadyToPublish()
        => PublishedEpisodes().Any() ? Result.Success() : SeriesErrors.PublishedEpisodeRequired;

    private IEnumerable<Episode> PublishedEpisodes()
        => _seasons.SelectMany(season => season.Episodes).Where(episode => episode.IsPublished);

    private Season? FindSeason(Guid seasonId) => _seasons.Find(season => season.Id == seasonId);

    private (Season Season, Episode Episode)? Find(Guid episodeId)
    {
        foreach (var season in _seasons)
        {
            if (season.Episodes.FirstOrDefault(episode => episode.Id == episodeId) is { } episode)
            {
                return (season, episode);
            }
        }

        return null;
    }
}
