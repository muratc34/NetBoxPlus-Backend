using Shared.Domain;

namespace Streaming.Domain.Playback;

/// <summary>
/// A user as someone watching: their playback sessions, of which only so many may be live at once.
/// Starting a session changes the version, so two starts at the same moment cannot both pass the
/// screen limit: the second one to save loses.
/// </summary>
public sealed class Viewer : AggregateRoot
{
    private readonly List<PlaybackSession> _sessions = [];

    private Viewer(Guid userId)
        : base(userId)
    {
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Viewer()
    {
    }

    public IReadOnlyCollection<PlaybackSession> Sessions => _sessions.AsReadOnly();

    /// <summary>Goes up with every session started; the concurrency token.</summary>
    public int Version { get; private set; }

    public static Viewer Create(Guid userId) => new(userId);

    public int LiveSessions(DateTimeOffset now) => _sessions.Count(session => session.IsLive(now));

    public Result<PlaybackSession> Start(PlaybackGrant grant, int maxScreens, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(grant);

        if (LiveSessions(now) >= maxScreens)
        {
            return PlaybackErrors.TooManyScreens;
        }

        var session = new PlaybackSession(Guid.CreateVersion7(), Id, grant, now);
        _sessions.Add(session);
        Version++;
        return session;
    }
}

/// <summary>What a session may play, decided when it starts.</summary>
/// <param name="MaxHeight">The tallest rendition the plan allows.</param>
public sealed record PlaybackGrant(Guid ProfileId, Guid ContentId, Guid TitleId, Guid VideoId, int MaxHeight);

/// <summary>
/// One screen watching one movie or episode. It stays live while heartbeats come; one missing for
/// <see cref="Timeout"/> ends it, and the screen is free again.
/// </summary>
public sealed class PlaybackSession : Entity
{
    /// <summary>How often a player sends a heartbeat.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>A session without a heartbeat for this long has ended.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    internal PlaybackSession(Guid id, Guid userId, PlaybackGrant grant, DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        ProfileId = grant.ProfileId;
        ContentId = grant.ContentId;
        TitleId = grant.TitleId;
        VideoId = grant.VideoId;
        MaxHeight = grant.MaxHeight;
        StartedAt = now;
        LastHeartbeatAt = now;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private PlaybackSession()
    {
    }

    public Guid UserId { get; private set; }

    public Guid ProfileId { get; private set; }

    /// <summary>The movie or episode being watched.</summary>
    public Guid ContentId { get; private set; }

    public Guid TitleId { get; private set; }

    public Guid VideoId { get; private set; }

    public int MaxHeight { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset LastHeartbeatAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public bool IsLive(DateTimeOffset now) => EndedAt is null && now - LastHeartbeatAt < Timeout;
}

public static class PlaybackErrors
{
    public static readonly Error ProfileNotFound = Error.NotFound("Playback.ProfileNotFound", "The profile was not found.");

    public static readonly Error SubscriptionRequired = Error.Forbidden("Playback.SubscriptionRequired", "Watching needs an active subscription.");

    public static readonly Error NotAvailable = Error.NotFound("Playback.NotAvailable", "This movie or episode cannot be watched.");

    public static readonly Error AgeRestricted = Error.Forbidden("Playback.AgeRestricted", "This profile may not watch this title.");

    public static readonly Error TooManyScreens = Error.Conflict("Playback.TooManyScreens", "The plan's screens are all in use. Stop watching on another screen first.");

    public static readonly Error SessionEnded = Error.NotFound("Playback.SessionEnded", "The playback session has ended or was not found.");

    public static readonly Error InvalidSignature = Error.Forbidden("Playback.InvalidSignature", "The address is not signed or its signature has expired.");
}

public interface IViewerRepository
{
    /// <summary>The viewer with their sessions, or <see langword="null"/> before their first one.</summary>
    Task<Viewer?> GetAsync(Guid userId, CancellationToken cancellationToken);

    void Add(Viewer viewer);
}

public interface IPlaybackSessionRepository
{
    /// <summary>
    /// Records a heartbeat if the session is the user's and still live. Immediate, atomic SQL instead
    /// of the aggregate: heartbeats of several screens of one user arrive together and must not
    /// conflict with each other or with a session being started.
    /// </summary>
    /// <returns>Whether the session was live and got the heartbeat.</returns>
    Task<bool> TryHeartbeatAsync(Guid sessionId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Ends the session if it is the user's. Immediate, atomic SQL: a session ended because access
    /// was lost must stay ended although the request that noticed it fails.
    /// </summary>
    /// <returns>Whether the user has such a session, ended now or before.</returns>
    Task<bool> EndAsync(Guid sessionId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
