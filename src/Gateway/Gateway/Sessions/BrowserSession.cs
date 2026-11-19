using System.Buffers.Text;
using System.Security.Cryptography;

namespace Gateway.Sessions;

/// <summary>Which application a session belongs to; each has its own cookie and route group.</summary>
public enum SessionKind
{
    Customer,
    Admin,
}

/// <summary>
/// What the gateway keeps for one signed-in browser: the token pair it got from the auth service
/// and the CSRF token the browser echoes. Stored encrypted; none of it reaches the browser except
/// the CSRF token.
/// </summary>
internal sealed record BrowserSession(
    SessionKind Kind,
    Guid UserId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    string CsrfToken,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// When the session last served a request. Kept apart from the encrypted part and written on
    /// its own, so that recording it never puts back tokens that a refresh has just replaced.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; init; } = CreatedAt;

    public bool NeedsRefresh(DateTimeOffset now, TimeSpan ahead) => AccessTokenExpiresAt - now <= ahead;

    /// <summary>The refresh token has run out, or an admin session sat idle for too long.</summary>
    public bool HasEnded(DateTimeOffset now, TimeSpan adminIdleTimeout)
        => RefreshTokenExpiresAt <= now || (Kind == SessionKind.Admin && LastSeenAt + adminIdleTimeout <= now);

    /// <summary>When the stored session may be dropped: the end of the refresh token, or of the idle time for admin sessions.</summary>
    public DateTimeOffset ExpiresAt(TimeSpan adminIdleTimeout)
        => Kind == SessionKind.Admin && LastSeenAt + adminIdleTimeout < RefreshTokenExpiresAt
            ? LastSeenAt + adminIdleTimeout
            : RefreshTokenExpiresAt;
}

/// <summary>Random values for session ids and CSRF tokens.</summary>
internal static class SessionSecrets
{
    /// <summary>256 bits, base64url: 43 characters.</summary>
    public const int Length = 43;

    public static string New() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static bool IsWellFormed(string? value) => value is { Length: Length } && Base64Url.IsValid(value);

    /// <summary>Constant-time comparison, so the response time does not reveal how much of a guess was right.</summary>
    public static bool AreEqual(string? presented, string expected)
        => presented is not null
            && CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(presented),
                System.Text.Encoding.ASCII.GetBytes(expected));
}
