namespace Gateway.ServerClients;

/// <summary>
/// The web application's server renders the catalog pages and calls the gateway for every visitor
/// from one address. It proves itself with this key and names the visitor in <c>X-Forwarded-For</c>,
/// so the rate limit counts visitors rather than the server. Bound from the <c>ServerClient</c>
/// section. Without a key no request is taken for the server's.
/// </summary>
public sealed class ServerClientOptions
{
    public const string SectionName = "ServerClient";

    /// <summary>Shorter keys are refused at startup: the key is all that separates the server from anyone else.</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>The secret the server sends in <see cref="ServerClientRequests.KeyHeader"/>. Never logged.</summary>
    public string? Key { get; set; }

    internal bool IsConfigured => !string.IsNullOrEmpty(Key);

    internal bool IsValid => !IsConfigured || Key!.Length >= MinimumKeyLength;
}
