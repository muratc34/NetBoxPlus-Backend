using Streaming.Domain.Access;

namespace Streaming.Domain.Playback;

/// <summary>How far a profile has watched a movie or an episode.</summary>
public sealed class WatchProgress
{
    /// <summary>The share at the end that counts as watched: credits, mostly.</summary>
    public const double FinishedShare = 0.05;

    /// <summary>Whatever the share, this much from the end is watched too, unless the video is short.</summary>
    public static readonly TimeSpan FinishedAtLeast = TimeSpan.FromMinutes(2);

    /// <summary>A short video counts as watched only near its end: at most this share of it.</summary>
    public const double FinishedAtMostShare = 0.10;

    private WatchProgress(Guid profileId, Guid contentId, Guid userId, Guid titleId, double positionSeconds, double durationSeconds, bool completed, DateTimeOffset updatedAt)
    {
        ProfileId = profileId;
        ContentId = contentId;
        UserId = userId;
        TitleId = titleId;
        PositionSeconds = positionSeconds;
        DurationSeconds = durationSeconds;
        Completed = completed;
        UpdatedAt = updatedAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private WatchProgress()
    {
    }

    public Guid ProfileId { get; private set; }

    public Guid ContentId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid TitleId { get; private set; }

    public double PositionSeconds { get; private set; }

    public double DurationSeconds { get; private set; }

    public bool Completed { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Where watching starts again: from the start once it was watched to the end.</summary>
    public double ResumeFromSeconds => Completed ? 0 : PositionSeconds;

    /// <summary>The position a session reports, kept within the video.</summary>
    public static WatchProgress Record(PlaybackSession session, double positionSeconds, double durationSeconds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);

        var position = Math.Clamp(positionSeconds, 0, Math.Max(durationSeconds, 0));
        return new WatchProgress(session.ProfileId, session.ContentId, session.UserId, session.TitleId, position, durationSeconds, IsFinished(position, durationSeconds), now);
    }

    /// <summary>
    /// Watched to the end: within the last 5% or the last 2 minutes, whichever is longer, but never
    /// more than the last 10%, so that a short video is not finished as soon as it starts.
    /// </summary>
    public static bool IsFinished(double positionSeconds, double durationSeconds)
    {
        if (durationSeconds <= 0)
        {
            return false;
        }

        var allowance = Math.Min(Math.Max(durationSeconds * FinishedShare, FinishedAtLeast.TotalSeconds), durationSeconds * FinishedAtMostShare);
        return durationSeconds - positionSeconds <= allowance;
    }
}

/// <summary>A row of "continue watching": the movie or episode to play next, and where it starts.</summary>
public sealed record ContinueWatchingEntry(Guid TitleId, TitleKind Kind, Guid ContentId, Guid VideoId, double PositionSeconds, DateTimeOffset UpdatedAt);

public static class ContinueWatching
{
    public const int MaxEntries = 20;

    /// <summary>
    /// One row per title, the most recently watched first: a movie not watched to the end, or a
    /// series at its unfinished episode, or at the next one when the last was finished. Only what
    /// can still be watched, and only what the profile may watch.
    /// </summary>
    /// <param name="progress">The profile's progress, most recent first.</param>
    /// <param name="titles">What can be watched now, by title id.</param>
    public static IReadOnlyList<ContinueWatchingEntry> Pick(
        IEnumerable<WatchProgress> progress,
        IReadOnlyDictionary<Guid, PlayableTitle> titles,
        ProfileCopy profile)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(profile);

        var entries = new List<ContinueWatchingEntry>();
        var seen = new HashSet<Guid>();
        foreach (var latest in progress)
        {
            if (!seen.Add(latest.TitleId) || !titles.TryGetValue(latest.TitleId, out var title) || !profile.MayWatch(title.AgeRating))
            {
                continue;
            }

            var item = title.Items.FirstOrDefault(candidate => candidate.ContentId == latest.ContentId);
            if (item is null)
            {
                continue;
            }

            if (!latest.Completed)
            {
                entries.Add(new ContinueWatchingEntry(title.Id, title.Kind, item.ContentId, item.VideoId, latest.PositionSeconds, latest.UpdatedAt));
            }
            else if (title.Kind == TitleKind.Series
                && title.Items.Where(candidate => candidate.Sequence > item.Sequence).MinBy(candidate => candidate.Sequence) is { } next)
            {
                entries.Add(new ContinueWatchingEntry(title.Id, title.Kind, next.ContentId, next.VideoId, 0, latest.UpdatedAt));
            }

            if (entries.Count == MaxEntries)
            {
                break;
            }
        }

        return entries;
    }
}

public interface IWatchProgressRepository
{
    /// <summary>
    /// Inserts or replaces the profile's progress on that content. Immediate, atomic SQL instead of a
    /// tracked change: it comes with heartbeats, which several screens send at once and which must
    /// not conflict; the last report wins.
    /// </summary>
    Task SaveAsync(WatchProgress progress, CancellationToken cancellationToken);

    Task<WatchProgress?> FindAsync(Guid profileId, Guid contentId, CancellationToken cancellationToken);

    /// <summary>Removes, with the change being saved, all progress of a profile that no longer exists.</summary>
    Task RemoveForProfileAsync(Guid profileId, CancellationToken cancellationToken);
}
