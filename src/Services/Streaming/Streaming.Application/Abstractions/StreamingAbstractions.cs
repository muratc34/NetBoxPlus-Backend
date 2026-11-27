using Streaming.Domain.Access;
using Streaming.Domain.Lists;
using Streaming.Domain.Playback;
using Streaming.Domain.Popularity;
using Streaming.Domain.Ratings;
using Streaming.Domain.Videos;

namespace Streaming.Application.Abstractions;

/// <summary>The locked profiles the caller's sign-in has opened with their PIN, from the access token.</summary>
public interface IUnlockedProfiles
{
    bool Contains(Guid profileId);
}

/// <summary>Outside a request (event handlers, background work) nothing is unlocked.</summary>
internal sealed class NoUnlockedProfiles : IUnlockedProfiles
{
    public bool Contains(Guid profileId) => false;
}

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

    /// <summary>The uploaded file as UTF-8 text; for small files such as subtitles.</summary>
    Task<string> ReadUploadTextAsync(Guid uploadId, CancellationToken cancellationToken);

    Task DeleteUploadAsync(Guid uploadId, CancellationToken cancellationToken);

    /// <summary>Writes a text file into the video's folder, replacing one already there.</summary>
    Task WriteTextAsync(Guid videoId, string path, string text, string contentType, CancellationToken cancellationToken);
}

/// <summary>Videos waiting for conversion; a line is added with the change that queued the video.</summary>
public interface ITranscodeQueue
{
    void Enqueue(Guid videoId, DateTimeOffset now);

    /// <summary>Takes the video out of line, with the change that finished it.</summary>
    void Remove(Guid videoId);

    /// <summary>Puts a dub of the video in line.</summary>
    void EnqueueAudioTrack(Guid videoId, Guid trackId, DateTimeOffset now);

    /// <summary>Takes the dub out of line, with the change that finished or removed it.</summary>
    void RemoveAudioTrack(Guid trackId);
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

    /// <summary>The titles on the profiles' lists.</summary>
    IQueryable<MyListEntry> MyListEntries { get; }

    /// <summary>The languages profiles chose in the player.</summary>
    IQueryable<PlaybackPreference> PlaybackPreferences { get; }

    /// <summary>How profiles rated titles.</summary>
    IQueryable<TitleRating> TitleRatings { get; }

    /// <summary>How many profiles started each title on the finished days of the last week.</summary>
    IQueryable<DailyPlays> DailyPlays { get; }
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

    /// <summary>The same for a trailer's playlists, bound to the video; never valid as a session's address, nor the other way round.</summary>
    string SignTrailer(Guid videoId, string path, DateTimeOffset expiresAt);

    bool IsValidTrailer(Guid videoId, string path, long? expires, string? signature, DateTimeOffset now);
}
