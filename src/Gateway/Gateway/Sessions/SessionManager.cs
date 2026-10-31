using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Gateway.Sessions;

/// <summary>A session found from the request's cookie.</summary>
internal sealed record LoadedSession(string Id, BrowserSession Session);

/// <summary>What happened to a session that needed fresh tokens.</summary>
internal enum RefreshStatus
{
    Fresh,
    Ended,
    Unavailable,
}

internal sealed record RefreshOutcome(RefreshStatus Status, BrowserSession? Session);

/// <summary>
/// Starts, reads, renews and ends browser sessions. Renewing is the delicate part: the auth service
/// rotates the refresh token on every use and treats a second use as theft, ending the whole
/// session. So a session is refreshed by one request at a time — concurrent requests on the same
/// instance share the refresh, and instances coordinate through a lock in Valkey.
/// </summary>
internal sealed partial class SessionManager(
    SessionStore store,
    AuthClient auth,
    SessionCookies cookies,
    IOptions<SessionOptions> options,
    TimeProvider timeProvider,
    ILogger<SessionManager> logger)
{
    /// <summary>Longer than the auth client's timeout, so the lock outlives any refresh it guards.</summary>
    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan LockPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How long a request waits for another instance's refresh before giving up.</summary>
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(12);

    /// <summary>Recording each request would write to Valkey on every call; once a minute is enough for an idle timeout.</summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Lazy<Task<RefreshOutcome>>> _refreshes = new(StringComparer.Ordinal);

    private SessionOptions Options => options.Value;

    /// <summary>
    /// The session of the request's cookie for <paramref name="kind"/>, or <see langword="null"/>
    /// without a cookie or when its session is gone. An ended session is deleted.
    /// </summary>
    public async Task<LoadedSession?> LoadAsync(HttpContext context, SessionKind kind)
    {
        var id = SessionCookies.Read(context, kind);

        if (!SessionSecrets.IsWellFormed(id))
        {
            return null;
        }

        var session = await store.GetAsync(id!).ConfigureAwait(false);
        if (session is null || session.Kind != kind)
        {
            return null;
        }

        if (session.HasEnded(timeProvider.GetUtcNow(), Options.AdminIdleTimeout))
        {
            await store.DeleteAsync(id!).ConfigureAwait(false);
            return null;
        }

        return new LoadedSession(id!, session);
    }

    /// <summary>A new session for a token pair just issued; any session the browser had before is ended.</summary>
    public async Task<LoadedSession> StartAsync(HttpContext context, SessionKind kind, TokenPair pair)
    {
        if (await LoadAsync(context, kind).ConfigureAwait(false) is { } previous)
        {
            await EndAsync(context, previous).ConfigureAwait(false);
        }

        // A new id at every sign-in: an id planted in the browser beforehand is never signed in.
        var id = SessionSecrets.New();
        var session = new BrowserSession(
            kind,
            UserIdOf(pair.AccessToken),
            pair.AccessToken,
            pair.AccessTokenExpiresAt,
            pair.RefreshToken,
            pair.RefreshTokenExpiresAt,
            SessionSecrets.New(),
            timeProvider.GetUtcNow());

        await store.SaveAsync(id, session).ConfigureAwait(false);
        cookies.Write(context, kind, id);
        return new LoadedSession(id, session);
    }

    /// <summary>Ends the session here and in the auth service; the cookie is cleared.</summary>
    public async Task EndAsync(HttpContext context, LoadedSession loaded)
    {
        await store.DeleteAsync(loaded.Id).ConfigureAwait(false);
        SessionCookies.Clear(context, loaded.Session.Kind);

        // Best effort: if the auth service is down, the refresh token is still useless, because
        // the only copy was in the session just deleted.
        var result = await auth.SignOutAsync(loaded.Session.RefreshToken, CancellationToken.None).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            LogSignOutFailed(logger, result.StatusCode);
        }
    }

    /// <summary>Records the request on an admin session, which ends after an idle period.</summary>
    public Task TouchAsync(LoadedSession loaded)
    {
        var now = timeProvider.GetUtcNow();
        if (loaded.Session.Kind != SessionKind.Admin || now - loaded.Session.LastSeenAt < TouchInterval)
        {
            return Task.CompletedTask;
        }

        return store.TouchAsync(loaded.Id, loaded.Session with { LastSeenAt = now });
    }

    /// <summary>The session with an access token that is good for at least <see cref="SessionOptions.RefreshAhead"/>.</summary>
    public Task<RefreshOutcome> EnsureFreshAsync(LoadedSession loaded)
    {
        if (!loaded.Session.NeedsRefresh(timeProvider.GetUtcNow(), Options.RefreshAhead))
        {
            return Task.FromResult(new RefreshOutcome(RefreshStatus.Fresh, loaded.Session));
        }

        // Requests on this instance share one refresh; the entry goes once it is done.
        var refresh = _refreshes.GetOrAdd(loaded.Id, id => new Lazy<Task<RefreshOutcome>>(() => RefreshAsync(id)));
        return AwaitAndForgetAsync(loaded.Id, refresh);
    }

    /// <summary>
    /// Runs <paramref name="change"/> on the session under its lock, for changes that replace its
    /// tokens (a password change). The function gets the session as stored now and returns the
    /// session to store, or <see langword="null"/> to leave it as it is.
    /// </summary>
    public async Task<T> ChangeAsync<T>(LoadedSession loaded, Func<BrowserSession, Task<(BrowserSession? Updated, T Result)>> change, T whenGone)
    {
        var owner = await WaitForLockAsync(loaded.Id).ConfigureAwait(false);
        if (owner is null)
        {
            return whenGone;
        }

        try
        {
            var current = await store.GetAsync(loaded.Id).ConfigureAwait(false);
            if (current is null)
            {
                return whenGone;
            }

            var (updated, result) = await change(current).ConfigureAwait(false);
            if (updated is not null)
            {
                await store.SaveAsync(loaded.Id, updated).ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            await store.ReleaseAsync(loaded.Id, owner).ConfigureAwait(false);
        }
    }

    /// <summary>The session with a new token pair; everything else stays.</summary>
    public static BrowserSession WithTokens(BrowserSession session, TokenPair pair) => session with
    {
        AccessToken = pair.AccessToken,
        AccessTokenExpiresAt = pair.AccessTokenExpiresAt,
        RefreshToken = pair.RefreshToken,
        RefreshTokenExpiresAt = pair.RefreshTokenExpiresAt,
    };

    private async Task<RefreshOutcome> AwaitAndForgetAsync(string id, Lazy<Task<RefreshOutcome>> refresh)
    {
        try
        {
            return await refresh.Value.ConfigureAwait(false);
        }
        finally
        {
            _refreshes.TryRemove(new KeyValuePair<string, Lazy<Task<RefreshOutcome>>>(id, refresh));
        }
    }

    // Never cancelled by the request that started it: stopping between the auth service rotating
    // the token and the session saving it would leave a used token in the session, and its next
    // use would end the session as stolen.
    private async Task<RefreshOutcome> RefreshAsync(string id)
    {
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            var owner = await store.TryLockAsync(id, LockExpiry).ConfigureAwait(false);
            if (owner is not null)
            {
                try
                {
                    return await RefreshLockedAsync(id).ConfigureAwait(false);
                }
                finally
                {
                    await store.ReleaseAsync(id, owner).ConfigureAwait(false);
                }
            }

            // Another instance is refreshing: its result appears in the store.
            await Task.Delay(LockPollInterval).ConfigureAwait(false);

            var current = await store.GetAsync(id).ConfigureAwait(false);
            if (current is null)
            {
                return new RefreshOutcome(RefreshStatus.Ended, null);
            }

            if (!current.NeedsRefresh(timeProvider.GetUtcNow(), Options.RefreshAhead))
            {
                return new RefreshOutcome(RefreshStatus.Fresh, current);
            }

            if (Stopwatch.GetElapsedTime(started) >= LockWait)
            {
                return new RefreshOutcome(RefreshStatus.Unavailable, null);
            }
        }
    }

    private async Task<RefreshOutcome> RefreshLockedAsync(string id)
    {
        // Read again under the lock: whoever held it before may have refreshed already.
        var current = await store.GetAsync(id).ConfigureAwait(false);
        if (current is null)
        {
            return new RefreshOutcome(RefreshStatus.Ended, null);
        }

        if (!current.NeedsRefresh(timeProvider.GetUtcNow(), Options.RefreshAhead))
        {
            return new RefreshOutcome(RefreshStatus.Fresh, current);
        }

        var result = await auth.RefreshAsync(current.RefreshToken, CancellationToken.None).ConfigureAwait(false);
        if (result.IsUnavailable)
        {
            return new RefreshOutcome(RefreshStatus.Unavailable, null);
        }

        if (result.Pair is null)
        {
            // Expired, revoked, reused or the account disabled: the session is over.
            LogRefreshRejected(logger, result.StatusCode);
            await store.DeleteAsync(id).ConfigureAwait(false);
            return new RefreshOutcome(RefreshStatus.Ended, null);
        }

        var renewed = WithTokens(current, result.Pair);
        await store.SaveAsync(id, renewed).ConfigureAwait(false);
        return new RefreshOutcome(RefreshStatus.Fresh, renewed);
    }

    private async Task<string?> WaitForLockAsync(string id)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (await store.TryLockAsync(id, LockExpiry).ConfigureAwait(false) is { } owner)
            {
                return owner;
            }

            if (Stopwatch.GetElapsedTime(started) >= LockWait)
            {
                return null;
            }

            await Task.Delay(LockPollInterval).ConfigureAwait(false);
        }
    }

    // The gateway received the token from the auth service itself, over its own connection, so it
    // reads the claims without validating the signature again; the services validate it.
    private static Guid UserIdOf(string accessToken)
        => Guid.Parse(new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(accessToken).Subject);

    [LoggerMessage(Level = LogLevel.Information, Message = "The auth service refused a session refresh with {StatusCode}; the session ended")]
    private static partial void LogRefreshRejected(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Signing out at the auth service failed with {StatusCode}; the session was ended on the gateway")]
    private static partial void LogSignOutFailed(ILogger logger, int statusCode);
}
