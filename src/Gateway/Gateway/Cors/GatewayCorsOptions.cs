namespace Gateway.Cors;

/// <summary>
/// Browser origins allowed to call the gateway, per route group. Bound from the <c>Cors</c>
/// section. An origin is a scheme, host and optional port, without a path or trailing slash, such
/// as <c>https://app.example.com</c>. Empty lists allow no cross-origin calls.
/// </summary>
public sealed class GatewayCorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>Origins of the customer-facing application.</summary>
    public string[] PublicOrigins { get; set; } = [];

    /// <summary>Origins of the admin panel. Kept apart, so the customer application cannot call admin routes.</summary>
    public string[] AdminOrigins { get; set; } = [];

    internal static bool IsValidOrigin(string origin)
        => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && uri.GetLeftPart(UriPartial.Authority).Equals(origin, StringComparison.OrdinalIgnoreCase);
}
