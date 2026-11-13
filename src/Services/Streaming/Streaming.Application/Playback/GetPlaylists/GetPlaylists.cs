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
/// <param name="Rendition">The rendition's folder, such as <c>720p</c>.</param>
public sealed record GetRenditionPlaylistQuery(Guid SessionId, string Rendition, long? Expires, string? Signature) : IQuery<string>;

internal sealed class PlaylistSource(IStreamingReadModel readModel, IPlaybackSigner signer, TimeProvider timeProvider)
{
    public DateTimeOffset Now { get; } = timeProvider.GetUtcNow();

    /// <summary>The session's video and the renditions it may play, after the address and the session are checked.</summary>
    public async Task<Result<(Video Video, List<Rendition> Renditions)>> OpenAsync(Guid sessionId, string path, long? expires, string? signature, CancellationToken cancellationToken)
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
        return renditions.Count == 0 ? PlaybackErrors.NotAvailable : (video!, renditions);
    }
}

internal sealed class GetMasterPlaylistHandler(PlaylistSource source, IPlaybackSigner signer) : IQueryHandler<GetMasterPlaylistQuery, string>
{
    public async Task<Result<string>> Handle(GetMasterPlaylistQuery query, CancellationToken cancellationToken)
    {
        var opened = await source.OpenAsync(query.SessionId, PlaybackLinks.MasterPlaylist, query.Expires, query.Signature, cancellationToken).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        var (video, renditions) = opened.Value;
        var expiresAt = source.Now + PlaybackLinks.LifetimeFor(video.DurationSeconds);
        var playlist = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n");
        foreach (var rendition in renditions)
        {
            playlist.Append(CultureInfo.InvariantCulture, $"#EXT-X-STREAM-INF:BANDWIDTH={rendition.Bandwidth},RESOLUTION={rendition.Width}x{rendition.Height}\n");
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

        var (video, renditions) = opened.Value;
        if (!renditions.Exists(rendition => rendition.Playlist == path))
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
