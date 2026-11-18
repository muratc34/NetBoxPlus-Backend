using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Streaming.Application.Videos.Subtitles;

/// <summary>
/// Reads uploaded subtitles: WebVTT is kept as it is, SRT is turned into WebVTT (players read only
/// WebVTT in HLS). Either needs at least one cue with a valid timing.
/// </summary>
public static partial class SubtitleFormat
{
    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>The WebVTT text, or <see langword="null"/> when the file is neither format.</summary>
    public static string? ToWebVtt(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = text.TrimStart(ByteOrderMark).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.StartsWith("WEBVTT", StringComparison.Ordinal))
        {
            return normalized.Split('\n').Any(line => VttTiming().IsMatch(line)) ? normalized.TrimEnd('\n') + "\n" : null;
        }

        var vtt = new StringBuilder("WEBVTT\n");
        var cues = 0;
        foreach (var block in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (lines.Count > 0 && int.TryParse(lines[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                lines.RemoveAt(0);
            }

            var timing = lines.Count > 1 ? SrtTiming().Match(lines[0].Trim()) : Match.Empty;
            if (!timing.Success)
            {
                return null;
            }

            vtt.Append('\n')
                .Append(CultureInfo.InvariantCulture, $"{Time(timing, 1)} --> {Time(timing, 5)}\n")
                .AppendJoin('\n', lines.Skip(1))
                .Append('\n');
            cues++;
        }

        return cues > 0 ? vtt.ToString() : null;
    }

    private static string Time(Match timing, int first)
    {
        var hours = int.Parse(timing.Groups[first].Value, CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{timing.Groups[first + 1].Value}:{timing.Groups[first + 2].Value}.{timing.Groups[first + 3].Value}");
    }

    [GeneratedRegex(@"^(\d{1,2}):([0-5]\d):([0-5]\d),(\d{3})\s*-->\s*(\d{1,2}):([0-5]\d):([0-5]\d),(\d{3})$", RegexOptions.CultureInvariant)]
    private static partial Regex SrtTiming();

    [GeneratedRegex(@"^(\d{2,}:)?[0-5]\d:[0-5]\d\.\d{3}\s+-->\s+(\d{2,}:)?[0-5]\d:[0-5]\d\.\d{3}", RegexOptions.CultureInvariant)]
    private static partial Regex VttTiming();
}
