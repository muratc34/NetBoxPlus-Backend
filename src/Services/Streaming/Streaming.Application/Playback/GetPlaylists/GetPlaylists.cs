using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Domain.Playback;
using Streaming.Domain.Videos;

namespace Streaming.Application.Playback.GetPlaylists;

/// <summary>The master playlist of a live session: the renditions its plan allows, each with a signed address.</summary>
public sealed record GetMasterPlaylistQuery(Guid SessionId, long? Expires, string? Signature) : IQuery<string>;

/// <summary>A rendition's playlist of a live session, its segments as read-only storage addresses.</summary>
/// <param name="Rendition">The rendition's folder, such as <c>720p</c> or <c>audio/original</c>.</param>
public sealed record GetRenditionPlaylistQuery(Guid SessionId, string Rendition, long? Expires, string? Signature) : IQuery<string>;

/// <summary>The playlist of one language of subtitles: the whole WebVTT file as a single segment.</summary>
/// <param name="SubtitleId">The subtitles' id, 32 hexadecimal digits.</param>
public sealed record GetSubtitlePlaylistQuery(Guid SessionId, string SubtitleId, long? Expires, string? Signature) : IQuery<string>;

/// <summary>What a session may be served of its video.</summary>
/// <param name="Audio">The ready audio tracks, the original first; none for a video whose sound is inside its renditions.</param>
internal sealed record SessionMedia(Video Video, List<Rendition> Renditions, List<AudioTrack> Audio, List<Subtitle> Subtitles);

internal sealed class PlaylistSource(IStreamingReadModel readModel, IPlaybackSigner signer, TimeProvider timeProvider)
{
    public DateTimeOffset Now { get; } = timeProvider.GetUtcNow();

    /// <summary>The session's video and what of it the session may play, after the address and the session are checked.</summary>
    public async Task<Result<SessionMedia>> OpenAsync(Guid sessionId, string path, long? expires, string? signature, CancellationToken cancellationToken)
    {
        if (!signer.IsValid(sessionId, path, expires, signature, Now))
        {
            return PlaybackErrors.InvalidSignature;
        }

        var session = await readModel.PlaybackSessions.FirstOrDefaultAsync(item => item.Id == sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null || !session.IsLive(Now))
        {
            return PlaybackErrors.SessionEnded;
        }

        var video = await readModel.Videos.FirstOrDefaultAsync(item => item.Id == session.VideoId && item.Status == VideoStatus.Ready, cancellationToken).ConfigureAwait(false);
        var renditions = video?.Renditions.Where(rendition => rendition.Height <= session.MaxHeight).OrderBy(rendition => rendition.Height).ToList() ?? [];
        if (renditions.Count == 0)
        {
            return PlaybackErrors.NotAvailable;
        }

        var audio = video!.AudioLayout == AudioLayout.Separate
            ? video.AudioTracks.Where(track => track.Status == TrackStatus.Ready && track.Playlist is not null)
                .OrderByDescending(track => track.IsOriginal)
                .ThenBy(track => track.Language, StringComparer.Ordinal)
                .ToList()
            : [];
        var subtitles = video.Subtitles.Where(subtitle => subtitle.Status == TrackStatus.Ready && subtitle.File is not null)
            .OrderBy(subtitle => subtitle.Language, StringComparer.Ordinal)
            .ToList();
        return new SessionMedia(video, renditions, audio, subtitles);
    }
}

internal sealed class GetMasterPlaylistHandler(PlaylistSource source, IPlaybackSigner signer) : IQueryHandler<GetMasterPlaylistQuery, string>
{
    private const string AudioGroup = "audio";

    private const string SubtitleGroup = "subtitles";

    public async Task<Result<string>> Handle(GetMasterPlaylistQuery query, CancellationToken cancellationToken)
    {
        var opened = await source.OpenAsync(query.SessionId, PlaybackLinks.MasterPlaylist, query.Expires, query.Signature, cancellationToken).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        var (video, renditions, audio, subtitles) = opened.Value;
        var expiresAt = source.Now + PlaybackLinks.LifetimeFor(video.DurationSeconds);
        var playlist = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n");

        // Each language of the sound is a rendition of its own, the original chosen unless the player picks another.
        foreach (var track in audio)
        {
            var isDefault = track.IsOriginal ? "YES" : "NO";
            playlist.Append(CultureInfo.InvariantCulture, $"#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"{AudioGroup}\",LANGUAGE=\"{track.Language}\",NAME=\"{MediaLanguages.Names[track.Language]}\",DEFAULT={isDefault},AUTOSELECT=YES,URI=\"{track.Playlist}?{signer.Sign(query.SessionId, track.Playlist!, expiresAt)}\"\n");
        }

        // Subtitles are off until the viewer turns them on.
        foreach (var subtitle in subtitles)
        {
            var path = PlaybackLinks.SubtitlePlaylist(subtitle.Id);
            playlist.Append(CultureInfo.InvariantCulture, $"#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=\"{SubtitleGroup}\",LANGUAGE=\"{subtitle.Language}\",NAME=\"{MediaLanguages.Names[subtitle.Language]}\",DEFAULT=NO,AUTOSELECT=YES,FORCED=NO,URI=\"{path}?{signer.Sign(query.SessionId, path, expiresAt)}\"\n");
        }

        var audioBandwidth = audio.Count == 0 ? 0 : audio.Max(track => track.Bandwidth ?? 0);
        var groups = (audio.Count == 0 ? string.Empty : $",AUDIO=\"{AudioGroup}\"") + (subtitles.Count == 0 ? string.Empty : $",SUBTITLES=\"{SubtitleGroup}\"");
        foreach (var rendition in renditions)
        {
            playlist.Append(CultureInfo.InvariantCulture, $"#EXT-X-STREAM-INF:BANDWIDTH={rendition.Bandwidth + audioBandwidth},RESOLUTION={rendition.Width}x{rendition.Height}{groups}\n");
            playlist.Append(CultureInfo.InvariantCulture, $"{rendition.Playlist}?{signer.Sign(query.SessionId, rendition.Playlist, expiresAt)}\n");
        }

        return playlist.ToString();
    }
}

internal sealed class GetRenditionPlaylistHandler(PlaylistSource source, IVideoStorage storage) : IQueryHandler<GetRenditionPlaylistQuery, string>
{
    private const string PlaylistName = "index.m3u8";

    public async Task<Result<string>> Handle(GetRenditionPlaylistQuery query, CancellationToken cancellationToken)
    {
        var path = $"{query.Rendition}/{PlaylistName}";
        var opened = await source.OpenAsync(query.SessionId, path, query.Expires, query.Signature, cancellationToken).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        var (video, renditions, audio, _) = opened.Value;
        if (!renditions.Exists(rendition => rendition.Playlist == path) && !audio.Exists(track => track.Playlist == path))
        {
            return PlaybackErrors.NotAvailable;
        }

        var text = await storage.ReadTextAsync(video.Id, path, cancellationToken).ConfigureAwait(false);
        if (text is null)
        {
            return PlaybackErrors.NotAvailable;
        }

        // Every line that is not a tag names a segment next to the playlist: it becomes a signed storage address.
        var expiresAt = source.Now + PlaybackLinks.LifetimeFor(video.DurationSeconds);
        var playlist = new StringBuilder();
        foreach (var line in text.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            playlist.Append(line.Length == 0 || line.StartsWith('#') ? line : storage.CreateReadAddress(video.Id, $"{query.Rendition}/{line}", expiresAt).AbsoluteUri);
            playlist.Append('\n');
        }

        return playlist.ToString().TrimEnd('\n') + "\n";
    }
}

internal sealed class GetSubtitlePlaylistHandler(PlaylistSource source, IVideoStorage storage) : IQueryHandler<GetSubtitlePlaylistQuery, string>
{
    public async Task<Result<string>> Handle(GetSubtitlePlaylistQuery query, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(query.SubtitleId, "N", out var subtitleId))
        {
            return PlaybackErrors.NotAvailable;
        }

        var opened = await source.OpenAsync(query.SessionId, PlaybackLinks.SubtitlePlaylist(subtitleId), query.Expires, query.Signature, cancellationToken).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        var subtitle = opened.Value.Subtitles.Find(item => item.Id == subtitleId);
        if (subtitle is null)
        {
            return PlaybackErrors.NotAvailable;
        }

        var video = opened.Value.Video;
        var duration = video.DurationSeconds ?? 0;
        var file = storage.CreateReadAddress(video.Id, subtitle.File!, source.Now + PlaybackLinks.LifetimeFor(video.DurationSeconds));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:{(int)Math.Ceiling(duration)}\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:VOD\n#EXTINF:{duration:0.000},\n{file.AbsoluteUri}\n#EXT-X-ENDLIST\n");
    }
}
