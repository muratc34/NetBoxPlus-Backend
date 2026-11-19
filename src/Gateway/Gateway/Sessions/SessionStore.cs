using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Gateway.Sessions;

/// <summary>
/// Sessions in Valkey, one hash per session: <c>data</c> holds the session encrypted with ASP.NET
/// Core Data Protection, <c>seen</c> the time of the last request. The key is the SHA-256 of the
/// session id, so neither a Valkey dump nor its key names give away a usable cookie. Every key
/// expires with its session.
/// </summary>
internal sealed class SessionStore(
    ValkeyConnection valkey,
    IDataProtectionProvider dataProtection,
    IOptions<SessionOptions> options,
    TimeProvider timeProvider)
{
    private const string DataField = "data";
    private const string SeenField = "seen";

    // Records the time only on a session that still exists; a plain HSET would recreate a deleted one.
    private const string TouchScript = """
        if redis.call('EXISTS', KEYS[1]) == 1 then
            redis.call('HSET', KEYS[1], 'seen', ARGV[1])
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        return 0
        """;

    // Releases the lock only if this caller still owns it, not one that took over after it expired.
    private const string ReleaseScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Gateway.BrowserSession.v1");

    public async Task<BrowserSession?> GetAsync(string sessionId)
    {
        var values = await valkey.Database.HashGetAsync(SessionKey(sessionId), [DataField, SeenField]).ConfigureAwait(false);
        if (values[0].IsNullOrEmpty || values[1].IsNullOrEmpty)
        {
            return null;
        }

        BrowserSession? session;
        try
        {
            session = JsonSerializer.Deserialize<BrowserSession>(_protector.Unprotect((byte[])values[0]!), JsonSerializerOptions.Web);
        }
        catch (CryptographicException)
        {
            // Written with a key that is gone: the session cannot be used any more.
            return null;
        }

        return session is null ? null : session with { LastSeenAt = DateTimeOffset.FromUnixTimeMilliseconds((long)values[1]) };
    }

    public async Task SaveAsync(string sessionId, BrowserSession session)
    {
        var key = SessionKey(sessionId);
        var data = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(session, JsonSerializerOptions.Web));

        var transaction = valkey.Database.CreateTransaction();
        _ = transaction.HashSetAsync(key, [new(DataField, data), new(SeenField, session.LastSeenAt.ToUnixTimeMilliseconds())]);
        _ = transaction.KeyExpireAsync(key, TimeToLive(session));
        await transaction.ExecuteAsync().ConfigureAwait(false);
    }

    /// <summary>Records a request on the session and moves its expiry, without rewriting its tokens.</summary>
    public Task TouchAsync(string sessionId, BrowserSession session)
        => valkey.Database.ScriptEvaluateAsync(
            TouchScript,
            [SessionKey(sessionId)],
            [session.LastSeenAt.ToUnixTimeMilliseconds(), (long)TimeToLive(session).TotalMilliseconds]);

    public Task DeleteAsync(string sessionId) => valkey.Database.KeyDeleteAsync(SessionKey(sessionId));

    /// <summary>
    /// Takes the session's lock for anything that replaces its tokens, so that two gateway instances
    /// never refresh the same session at once. Returns the owner value, or <see langword="null"/>
    /// when another request holds the lock. It expires on its own if the holder dies.
    /// </summary>
    public async Task<string?> TryLockAsync(string sessionId, TimeSpan expiry)
    {
        var owner = SessionSecrets.New();
        return await valkey.Database.StringSetAsync(LockKey(sessionId), owner, expiry, When.NotExists).ConfigureAwait(false)
            ? owner
            : null;
    }

    public Task ReleaseAsync(string sessionId, string owner)
        => valkey.Database.ScriptEvaluateAsync(ReleaseScript, [LockKey(sessionId)], [owner]);

    private TimeSpan TimeToLive(BrowserSession session)
    {
        var remaining = session.ExpiresAt(options.Value.AdminIdleTimeout) - timeProvider.GetUtcNow();
        return remaining > TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
    }

    private static RedisKey SessionKey(string sessionId) => "gateway:session:" + Hash(sessionId);

    private static RedisKey LockKey(string sessionId) => "gateway:session-lock:" + Hash(sessionId);

    private static string Hash(string sessionId) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(sessionId)));
}
