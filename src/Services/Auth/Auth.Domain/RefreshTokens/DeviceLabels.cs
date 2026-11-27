namespace Auth.Domain.RefreshTokens;

/// <summary>
/// Names a sign-in's device from its <c>User-Agent</c>, for the account's list of devices: the browser and the
/// operating system, such as "Chrome · Windows". Only the name is kept; the header itself, which identifies a
/// browser more closely, is not. Recognizes the common browsers and systems; anything else is "unknown".
/// </summary>
public static class DeviceLabels
{
    public const int MaxLength = 64;

    // In order: the first match wins, so the browsers built on Chrome come before it and Chrome before Safari
    // (every Chrome also says "Safari").
    private static readonly (string Marker, string Name)[] Browsers =
    [
        ("Edg/", "Edge"),
        ("EdgA/", "Edge"),
        ("EdgiOS/", "Edge"),
        ("OPR/", "Opera"),
        ("SamsungBrowser/", "Samsung Internet"),
        ("Firefox/", "Firefox"),
        ("FxiOS/", "Firefox"),
        ("CriOS/", "Chrome"),
        ("Chrome/", "Chrome"),
        ("Safari/", "Safari"),
    ];

    // iPads and iPhones say "like Mac OS X", and Android says "Linux": the specific ones come first.
    private static readonly (string Marker, string Name)[] Systems =
    [
        ("iPad", "iPadOS"),
        ("iPhone", "iOS"),
        ("Android", "Android"),
        ("Windows", "Windows"),
        ("CrOS", "ChromeOS"),
        ("Mac OS X", "macOS"),
        ("Macintosh", "macOS"),
        ("Linux", "Linux"),
    ];

    /// <summary>"Browser · System", either one alone, or <see langword="null"/> when neither is recognized.</summary>
    public static string? FromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var browser = First(Browsers, userAgent);
        var system = First(Systems, userAgent);
        return (browser, system) switch
        {
            (null, null) => null,
            (not null, not null) => $"{browser} · {system}",
            _ => browser ?? system,
        };
    }

    /// <summary>A label as it is stored: trimmed, at most <see cref="MaxLength"/> characters, <see langword="null"/> when empty.</summary>
    public static string? Clean(string? label)
    {
        var trimmed = label?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= MaxLength ? trimmed : trimmed[..MaxLength];
    }

    private static string? First((string Marker, string Name)[] candidates, string userAgent)
        => candidates.FirstOrDefault(candidate => userAgent.Contains(candidate.Marker, StringComparison.Ordinal)).Name;
}
