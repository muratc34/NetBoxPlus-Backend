using Streaming.Domain.Access;
using Streaming.Domain.Playback;
using Streaming.Domain.Videos;

namespace Streaming.Application.Abstractions;

/// <summary>Where uploaded video files arrive.</summary>
public interface IVideoStorage
{
    /// <summary>A write-only address for the upload, good until <paramref name="expiresAt"/>.</summary>
    Uri CreateUploadAddress(Guid videoId, DateTimeOffset expiresAt);

    /// <summary>The text of a converted file, such as a playlist, or <see langword="null"/> when there is none.</summary>
    Task<string?> ReadTextAsync(Guid videoId, string path, CancellationToken cancellationToken);

    /// <summary>A read-only address of a converted file, good until <paramref name="expiresAt"/>.</summary>
    Uri CreateReadAddress(Guid videoId, string path, DateTimeOffset expiresAt);

    /// <summary>The size of the uploaded file, or <see langword="null"/> when nothing has arrived.</summary>
    Task<long?> GetUploadSizeAsync(Guid videoId, CancellationToken cancellationToken);
}

/// <summary>Videos waiting for conversion; a line is added with the change that queued the video.</summary>
public interface ITranscodeQueue
{
    void Enqueue(Guid videoId, DateTimeOffset now);

    /// <summary>Takes the video out of line, with the change that finished it.</summary>
    void Remove(Guid videoId);
}

/// <summary>Untracked queryable views for query handlers.</summary>
public interface IStreamingReadModel
{
    IQueryable<Video> Videos { get; }

    IQueryable<PlaybackSession> PlaybackSessions { get; }

    IQueryable<WatchProgress> WatchProgress { get; }

    /// <summary>Copies of the billing service's subscriptions.</summary>
    IQueryable<SubscriptionCopy> Subscriptions { get; }

    /// <summary>Copies of the account service's profiles, removed ones included.</summary>
    IQueryable<ProfileCopy> Profiles { get; }

    /// <summary>Copies of what the catalog says can be watched.</summary>
    IQueryable<PlayableTitle> PlayableTitles { get; }
}

/// <summary>
/// Signs the addresses of a session's playlists, so that a player can fetch them without a token:
/// the signature binds the session, the file and an expiry.
/// </summary>
public interface IPlaybackSigner
{
    /// <summary>The query string (<c>exp=…&amp;sig=…</c>) that makes the address valid until <paramref name="expiresAt"/>.</summary>
    string Sign(Guid sessionId, string path, DateTimeOffset expiresAt);

    bool IsValid(Guid sessionId, string path, long? expires, string? signature, DateTimeOffset now);
}
