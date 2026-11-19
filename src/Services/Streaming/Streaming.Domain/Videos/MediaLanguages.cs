using System.Collections.Frozen;

namespace Streaming.Domain.Videos;

/// <summary>
/// The languages an audio track or a subtitle can be in: BCP 47 tags with a region, as HLS players
/// expect them, each with its name in its own language for the player's menu. Adding one is a line.
/// </summary>
public static class MediaLanguages
{
    public const string Default = "tr-TR";

    /// <summary>The longest tag, for column sizes.</summary>
    public const int MaxLength = 16;

    public static IReadOnlyDictionary<string, string> Names { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["tr-TR"] = "Türkçe",
        ["en-US"] = "English (US)",
        ["en-GB"] = "English (UK)",
        ["de-DE"] = "Deutsch",
        ["fr-FR"] = "Français",
        ["es-ES"] = "Español",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool IsSupported(string? language) => language is not null && Names.ContainsKey(language);
}
