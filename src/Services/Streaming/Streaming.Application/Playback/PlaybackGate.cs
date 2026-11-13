using Microsoft.EntityFrameworkCore;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Domain.Access;
using Streaming.Domain.Playback;
using Streaming.Domain.Videos;

namespace Streaming.Application.Playback;

/// <summary>What a session may play and how many screens the plan allows.</summary>
internal sealed record PlaybackAccess(PlaybackGrant Grant, int MaxScreens);

/// <summary>
/// Decides from the local copies whether a profile may watch a movie or episode now: the profile is
/// the user's, a subscription is active, the content can be watched and its age rating fits.
/// Asked when a session starts and with every heartbeat.
/// </summary>
internal sealed class PlaybackGate(IStreamingReadModel readModel)
{
    public async Task<Result<PlaybackAccess>> CheckAsync(Guid userId, Guid profileId, Guid contentId, CancellationToken cancellationToken)
    {
        var profile = await readModel.Profiles.FirstOrDefaultAsync(item => item.Id == profileId && item.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (profile is null || profile.IsRemoved)
        {
            return PlaybackErrors.ProfileNotFound;
        }

        var subscription = await readModel.Subscriptions
            .Where(item => item.UserId == userId && item.Status == SubscriptionStatus.Active)
            .OrderByDescending(item => item.MaxScreens)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (subscription is null)
        {
            return PlaybackErrors.SubscriptionRequired;
        }

        var playable = await readModel.PlayableTitles
            .SelectMany(title => title.Items, (title, item) => new { title.Id, title.AgeRating, item.ContentId, item.VideoId })
            .FirstOrDefaultAsync(item => item.ContentId == contentId, cancellationToken).ConfigureAwait(false);
        var ready = playable is not null
            && await readModel.Videos.AnyAsync(video => video.Id == playable.VideoId && video.Status == VideoStatus.Ready, cancellationToken).ConfigureAwait(false);
        if (!ready)
        {
            return PlaybackErrors.NotAvailable;
        }

        if (!profile.MayWatch(playable!.AgeRating))
        {
            return PlaybackErrors.AgeRestricted;
        }

        var grant = new PlaybackGrant(profileId, contentId, playable.Id, playable.VideoId, VideoQualities.MaxHeight(subscription.Quality));
        return new PlaybackAccess(grant, subscription.MaxScreens);
    }
}

/// <summary>How long the addresses handed to a player stay valid.</summary>
public static class PlaybackLinks
{
    /// <summary>The master playlist of a new session: fetched right away.</summary>
    public static readonly TimeSpan MasterLifetime = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(6);

    public const string MasterPlaylist = "master.m3u8";

    /// <summary>
    /// Rendition playlists and segments: players read a finished video's playlist once, so its
    /// addresses must last the whole video, with time for pauses; at most six hours.
    /// </summary>
    public static TimeSpan LifetimeFor(double? durationSeconds)
    {
        var lifetime = TimeSpan.FromSeconds(durationSeconds ?? 0) + TimeSpan.FromMinutes(30);
        return lifetime < MaxLifetime ? lifetime : MaxLifetime;
    }
}
