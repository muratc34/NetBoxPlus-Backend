namespace Auth.Infrastructure.Security.Tokens;

/// <summary>
/// Access token settings, from the <c>Auth:Tokens</c> section. The signing keys are secrets:
/// locally <c>./deploy/dev.ps1 secrets</c> creates one in the user secrets, elsewhere they come
/// from the secret store.
/// </summary>
public sealed class AccessTokenOptions
{
    public const string SectionName = "Auth:Tokens";

    /// <summary>
    /// The <c>iss</c> claim, also published as <c>issuer</c> in the discovery document. A logical
    /// name rather than a URL, so it does not change with the host name.
    /// </summary>
    public string Issuer { get; set; } = "netboxplus-auth";

    /// <summary>The <c>aud</c> claim; every service accepts it.</summary>
    public string Audience { get; set; } = "netboxplus";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Key id of the key that signs new tokens; must be one of <see cref="SigningKeys"/>.</summary>
    public string ActiveKeyId { get; set; } = string.Empty;

    /// <summary>
    /// Key id → P-256 private key in PEM. Every key is published in the JWKS, so tokens signed with
    /// a key that is no longer active keep validating until the key is removed.
    /// </summary>
    public Dictionary<string, string> SigningKeys { get; } = new(StringComparer.Ordinal);
}
