using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Domain.Access;
using Streaming.Domain.Playback;

namespace Streaming.Application.Playback.GetProgress;

/// <summary>How far one of the user's profiles has watched each movie or episode of a title.</summary>
public sealed record GetProgressQuery(Guid UserId, Guid ProfileId, Guid TitleId) : IQuery<IReadOnlyList<ProgressView>>;

public sealed record ProgressView(Guid ContentId, double PositionSeconds, double DurationSeconds, bool Completed, DateTimeOffset UpdatedAt);

/// <summary>What one of the user's profiles was watching, most recent first: at most 20 titles.</summary>
public sealed record GetContinueWatchingQuery(Guid UserId, Guid ProfileId) : IQuery<IReadOnlyList<ContinueWatchingItem>>;

/// <param name="ContentId">The movie, or the episode to go on with.</param>
/// <param name="PositionSeconds">Where to start: 0 for the next episode.</param>
public sealed record ContinueWatchingItem(Guid TitleId, TitleKind Kind, Guid ContentId, double PositionSeconds, double DurationSeconds, DateTimeOffset UpdatedAt);

internal static class ProfileOwnership
{
    public static Task<ProfileCopy?> FindAsync(IStreamingReadModel readModel, Guid userId, Guid profileId, CancellationToken cancellationToken)
        => readModel.Profiles.FirstOrDefaultAsync(profile => profile.Id == profileId && profile.UserId == userId && !profile.IsRemoved, cancellationToken);
}

internal sealed class GetProgressHandler(IStreamingReadModel readModel) : IQueryHandler<GetProgressQuery, IReadOnlyList<ProgressView>>
{
    public async Task<Result<IReadOnlyList<ProgressView>>> Handle(GetProgressQuery query, CancellationToken cancellationToken)
    {
        if (await ProfileOwnership.FindAsync(readModel, query.UserId, query.ProfileId, cancellationToken).ConfigureAwait(false) is null)
        {
            return PlaybackErrors.ProfileNotFound;
        }

        var views = await readModel.WatchProgress
            .Where(progress => progress.ProfileId == query.ProfileId && progress.TitleId == query.TitleId)
            .OrderByDescending(progress => progress.UpdatedAt)
            .Select(progress => new ProgressView(progress.ContentId, progress.PositionSeconds, progress.DurationSeconds, progress.Completed, progress.UpdatedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return views;
    }
}

internal sealed class GetContinueWatchingHandler(IStreamingReadModel readModel) : IQueryHandler<GetContinueWatchingQuery, IReadOnlyList<ContinueWatchingItem>>
{
    // Enough recent rows to fill the list even when some titles are gone or done.
    private const int RecentRows = 200;

    public async Task<Result<IReadOnlyList<ContinueWatchingItem>>> Handle(GetContinueWatchingQuery query, CancellationToken cancellationToken)
    {
        var profile = await ProfileOwnership.FindAsync(readModel, query.UserId, query.ProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return PlaybackErrors.ProfileNotFound;
        }

        var recent = await readModel.WatchProgress
            .Where(progress => progress.ProfileId == query.ProfileId)
            .OrderByDescending(progress => progress.UpdatedAt)
            .Take(RecentRows)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var titleIds = recent.Select(progress => progress.TitleId).Distinct().ToList();
        var titles = await readModel.PlayableTitles.Where(title => titleIds.Contains(title.Id)).ToDictionaryAsync(title => title.Id, cancellationToken).ConfigureAwait(false);

        var entries = ContinueWatching.Pick(recent, titles, profile);
        var videoIds = entries.Select(entry => entry.VideoId).ToList();
        var durations = await readModel.Videos.Where(video => videoIds.Contains(video.Id))
            .ToDictionaryAsync(video => video.Id, video => video.DurationSeconds ?? 0, cancellationToken).ConfigureAwait(false);

        return entries
            .Select(entry => new ContinueWatchingItem(entry.TitleId, entry.Kind, entry.ContentId, entry.PositionSeconds, durations.GetValueOrDefault(entry.VideoId), entry.UpdatedAt))
            .ToList();
    }
}
