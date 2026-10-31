namespace Gateway.Sessions;

/// <summary>Browser sessions. Bound from the <c>Session</c> section.</summary>
public sealed class SessionOptions
{
    public const string SectionName = "Session";

    /// <summary>
    /// The auth service, which the gateway calls itself to sign in, refresh and sign out. Its token
    /// endpoints are never proxied: only the gateway may receive tokens.
    /// </summary>
    public Uri? AuthAddress { get; set; }

    /// <summary>An admin session ends after this long without a request.</summary>
    public TimeSpan AdminIdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>An access token this close to expiry is refreshed before the request is forwarded.</summary>
    public TimeSpan RefreshAhead { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Lifetime of the customer cookie; the session itself ends with its refresh token, usually sooner.</summary>
    public TimeSpan CustomerCookieLifetime { get; set; } = TimeSpan.FromDays(30);

    internal bool IsValid
        => AuthAddress is { IsAbsoluteUri: true }
            && AdminIdleTimeout > TimeSpan.Zero
            && RefreshAhead >= TimeSpan.Zero
            && CustomerCookieLifetime > TimeSpan.Zero;
}
