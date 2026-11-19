using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Application.Playback;
using Streaming.Domain.Access;
using Streaming.Domain.Playback;
using Streaming.Domain.Videos;

namespace Streaming.Application.Trailers;

/// <summary>The trailer of a movie or a series, for anyone: no session, subscription or screen limit.</summary>
public sealed record GetTrailerQuery(Guid TitleId) : IQuery<TrailerLink>;

/// <param name="Playlist">The master playlist's address, relative to the service's root and signed until <paramref name="ExpiresAt"/>.</param>
public sealed record TrailerLink(Guid VideoId, string Playlist, double DurationSeconds, DateTimeOffset ExpiresAt);

/// <summary>A trailer's master playlist: its renditions and its sound, each with a signed address.</summary>
public sealed record GetTrailerMasterPlaylistQuery(Guid VideoId, long? Expires, string? Signature) : IQuery<string>;

/// <summary>One rendition or the sound of a trailer, its segments as read-only storage addresses.</summary>
/// <param name="Rendition">The rendition's folder, such as <c>720p</c> or <c>audio/original</c>.</param>
public sealed record GetTrailerRenditionPlaylistQuery(Guid VideoId, string Rendition, long? Expires, string? Signature) : IQuery<string>;

public static class TrailerErrors
{
    public static readonly Error NotFound = Error.NotFound("Trailer.NotFound", "This title has no trailer that can be played.");
}

public static class TrailerLinks
{
    /// <summary>Long enough to watch a trailer twice over; links are fetched afresh with every page view.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public static string MasterPlaylist(Guid videoId) => $"trailers/{videoId}/{PlaybackLinks.MasterPlaylist}";
}

/// <summary>A trailer that can be played now: ready, and of a title someone can watch, so an unpublished title's stays hidden.</summary>
internal sealed class TrailerSource(IStreamingReadModel readModel, IPlaybackSigner signer, TimeProvider timeProvider)
{
    public DateTimeOffset Now { get; } = timeProvider.GetUtcNow();

    /// <summary>The newest ready trailer of the title, if the title can be watched.</summary>
    public async Task<Video?> FindAsync(Guid titleId, CancellationToken cancellationToken)
    {
        var title = await readModel.PlayableTitles.FirstOrDefaultAsync(item => item.Id == titleId, cancellationToken).ConfigureAwait(false);
        if (title is null || title.Items.Count == 0)
        {
            return null;
        }

        var kind = title.Kind == TitleKind.Movie ? ContentKind.Movie : ContentKind.Series;
        return await readModel.Videos
            .Where(video => video.Purpose == VideoPurpose.Trailer && video.ContentKind == kind && video.ContentId == titleId && video.Status == VideoStatus.Ready)
            .OrderByDescending(video => video.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The trailer behind a signed playlist address, checked again: a title taken off makes its trailer's links stop.</summary>
    public async Task<Result<Video>> OpenAsync(Guid videoId, string path, long? expires, string? signature, CancellationToken cancellationToken)
    {
        if (!signer.IsValidTrailer(videoId, path, expires, signature, Now))
        {
            return PlaybackErrors.InvalidSignature;
        }

        var video = await readModel.Videos
            .FirstOrDefaultAsync(item => item.Id == videoId && item.Purpose == VideoPurpose.Trailer && item.Status == VideoStatus.Ready, cancellationToken)
            .ConfigureAwait(false);
        if (video is null || await FindAsync(video.ContentId, cancellationToken).ConfigureAwait(false) is not { } current || current.Id != video.Id)
        {
            return TrailerErrors.NotFound;
        }

        return video;
    }

    /// <summary>What a player may fetch of the trailer: renditions up to the trailer limit and the original sound.</summary>
    public static (List<Rendition> Renditions, AudioTrack? Sound) Media(Video video)
        => (
            [.. video.Renditions.Where(rendition => rendition.Height <= Video.TrailerMaxHeight).OrderBy(rendition => rendition.Height)],
            video.AudioLayout == AudioLayout.Separate
                ? video.AudioTracks.FirstOrDefault(track => track.IsOriginal && track.Status == TrackStatus.Ready && track.Playlist is not null)
                : null);
}

internal sealed class GetTrailerHandler(TrailerSource source, IPlaybackSigner signer, StreamingMetrics metrics) : IQueryHandler<GetTrailerQuery, TrailerLink>
{
    public async Task<Result<TrailerLink>> Handle(GetTrailerQuery query, CancellationToken cancellationToken)
    {
        var trailer = await source.FindAsync(query.TitleId, cancellationToken).ConfigureAwait(false);
        if (trailer is null)
        {
            metrics.TrailerRequested("not_found");
            return TrailerErrors.NotFound;
        }

        metrics.TrailerRequested("found");
        var expiresAt = source.Now + TrailerLinks.Lifetime;
        var signed = signer.SignTrailer(trailer.Id, PlaybackLinks.MasterPlaylist, expiresAt);
        return new TrailerLink(trailer.Id, $"{TrailerLinks.MasterPlaylist(trailer.Id)}?{signed}", trailer.DurationSeconds ?? 0, expiresAt);
    }
}

internal sealed class GetTrailerMasterPlaylistHandler(TrailerSource source, IPlaybackSigner signer) : IQueryHandler<GetTrailerMasterPlaylistQuery, string>
{
    private const string AudioGroup = "audio";

    public async Task<Result<string>> Handle(GetTrailerMasterPlaylistQuery query, CancellationToken cancellationToken)
    {
        var opened = await source.OpenAsync(query.VideoId, PlaybackLinks.MasterPlaylist, query.Expires, query.Signature, cancellationToken).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        var video = opened.Value;
        var (renditions, sound) = TrailerSource.Media(video);
        if (renditions.Count == 0)
        {
            return TrailerErrors.NotFound;
        }

        var expiresAt = source.Now + TrailerLinks.Lifetime;
        var playlist = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n");
        if (sound is not null)
        {
            playlist.Append(CultureInfo.InvariantCulture, $"#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"{AudioGroup}\",LANGUAGE=\"{sound.Language}\",NAME=\"{MediaLanguages.Names[sound.Language]}\",DEFAULT=YES,AUTOSELECT=YES,URI=\"{sound.Playlist}?{signer.SignTrailer(video.Id, sound.Playlist!, expiresAt)}\"\n");
        }

        var group = sound is null ? string.Empty : $",AUDIO=\"{AudioGroup}\"";
        var soundBandwidth = sound?.Bandwidth ?? 0;
        foreach (var rendition in renditions)
        {
            playlist.Append(CultureInfo.InvariantCulture, $"#EXT-X-STREAM-INF:BANDWIDTH={rendition.Bandwidth + soundBandwidth},RESOLUTION={rendition.Width}x{rendition.Height}{group}\n");
            playlist.Append(CultureInfo.InvariantCulture, $"{rendition.Playlist}?{signer.SignTrailer(video.Id, rendition.Playlist, expiresAt)}\n");
        }

        return playlist.ToString();
    }
}

internal sealed class GetTrailerRenditionPlaylistHandler(TrailerSource source, IVideoStorage storage) : IQueryHandler<GetTrailerRenditionPlaylistQuery, string>
{
    private const string PlaylistName = "index.m3u8";

    public async Task<Result<string>> Handle(GetTrailerRenditionPlaylistQuery query, CancellationToken cancellationToken)
    {
        var path = $"{query.Rendition}/{PlaylistName}";
        var opened = await source.OpenAsync(query.VideoId, path, query.Expires, query.Signature, cancellationToken).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        var video = opened.Value;
        var (renditions, sound) = TrailerSource.Media(video);
        if (!renditions.Exists(rendition => rendition.Playlist == path) && sound?.Playlist != path)
        {
            return TrailerErrors.NotFound;
        }

        var text = await storage.ReadTextAsync(video.Id, path, cancellationToken).ConfigureAwait(false);
        if (text is null)
        {
            return TrailerErrors.NotFound;
        }

        // Every line that is not a tag names a segment next to the playlist: it becomes a signed storage address.
        var expiresAt = source.Now + TrailerLinks.Lifetime;
        var playlist = new StringBuilder();
        foreach (var line in text.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            playlist.Append(line.Length == 0 || line.StartsWith('#') ? line : storage.CreateReadAddress(video.Id, $"{query.Rendition}/{line}", expiresAt).AbsoluteUri);
            playlist.Append('\n');
        }

        return playlist.ToString().TrimEnd('\n') + "\n";
    }
}
