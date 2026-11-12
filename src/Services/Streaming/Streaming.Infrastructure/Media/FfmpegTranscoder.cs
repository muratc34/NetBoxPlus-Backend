using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Streaming.Domain.Videos;

namespace Streaming.Infrastructure.Media;

/// <summary>The <c>Transcoder</c> section.</summary>
public sealed class TranscoderOptions
{
    public const string SectionName = "Transcoder";

    /// <summary>The ffmpeg program; found on the PATH by default.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>The ffprobe program; found on the PATH by default.</summary>
    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>Where work files go; the system's temporary folder by default.</summary>
    public string? WorkDirectory { get; set; }
}

/// <summary>What converting a file gave.</summary>
public abstract record TranscodeOutcome
{
    public sealed record Converted(double DurationSeconds, IReadOnlyList<Rendition> Renditions) : TranscodeOutcome;

    /// <summary>The file itself is the problem: trying again cannot help.</summary>
    public sealed record Rejected(string Reason) : TranscodeOutcome;
}

/// <summary>
/// Turns a video file into HLS with ffmpeg: one rendition per rung of the ladder up to the source's
/// height, each a playlist of six-second MPEG-TS segments, and a master playlist over them.
/// </summary>
public sealed class FfmpegTranscoder(IOptions<TranscoderOptions> options)
{
    private const int AudioKbps = 128;

    /// <summary>Heights and video bitrates; a source is never scaled up.</summary>
    private static readonly (int Height, int Kbps)[] Ladder = [(360, 800), (720, 2800), (1080, 5000)];

    public async Task<TranscodeOutcome> TranscodeAsync(string source, string outputDirectory, CancellationToken cancellationToken)
    {
        var probe = await ProbeAsync(source, cancellationToken).ConfigureAwait(false);
        if (probe is null)
        {
            return new TranscodeOutcome.Rejected("The file is not a readable video.");
        }

        if (probe.Width <= 0 || probe.Height <= 0 || probe.DurationSeconds <= 0)
        {
            return new TranscodeOutcome.Rejected("The file has no picture to convert.");
        }

        var rungs = Ladder.Where(rung => rung.Height <= probe.Height).ToList();
        if (rungs.Count == 0)
        {
            // Smaller than the lowest rung: kept at its own (even) height.
            rungs.Add((probe.Height / 2 * 2, Ladder[0].Kbps));
        }

        var renditions = new List<Rendition>();
        foreach (var (height, kbps) in rungs)
        {
            var folder = $"{height}p";
            var directory = Directory.CreateDirectory(Path.Combine(outputDirectory, folder)).FullName;
            var arguments = new List<string>
            {
                "-hide_banner", "-nostdin", "-y", "-i", source,
                "-vf", $"scale=-2:{height}",
                "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "main", "-pix_fmt", "yuv420p",
                "-b:v", $"{kbps}k", "-maxrate", $"{kbps * 107 / 100}k", "-bufsize", $"{kbps * 3 / 2}k",
                "-g", "48", "-keyint_min", "48", "-sc_threshold", "0",
            };
            arguments.AddRange(probe.HasAudio ? ["-c:a", "aac", "-b:a", $"{AudioKbps}k", "-ac", "2"] : ["-an"]);
            arguments.AddRange(["-f", "hls", "-hls_time", "6", "-hls_playlist_type", "vod",
                "-hls_segment_filename", Path.Combine(directory, "seg_%04d.ts"), Path.Combine(directory, "index.m3u8")]);

            var run = await RunAsync(options.Value.FfmpegPath, arguments, cancellationToken).ConfigureAwait(false);
            if (run.ExitCode != 0)
            {
                return new TranscodeOutcome.Rejected($"The video could not be converted to {height}p.");
            }

            var width = (int)Math.Round(probe.Width * (double)height / probe.Height / 2) * 2;
            renditions.Add(new Rendition(height, width, (kbps + (probe.HasAudio ? AudioKbps : 0)) * 1000, $"{folder}/index.m3u8"));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "master.m3u8"), MasterPlaylist(renditions), Encoding.ASCII, cancellationToken).ConfigureAwait(false);
        return new TranscodeOutcome.Converted(probe.DurationSeconds, renditions);
    }

    public static string MasterPlaylist(IEnumerable<Rendition> renditions)
    {
        var playlist = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n");
        foreach (var rendition in renditions.OrderBy(item => item.Height))
        {
            playlist.Append(CultureInfo.InvariantCulture, $"#EXT-X-STREAM-INF:BANDWIDTH={rendition.Bandwidth},RESOLUTION={rendition.Width}x{rendition.Height}\n{rendition.Playlist}\n");
        }

        return playlist.ToString();
    }

    private sealed record Probe(int Width, int Height, double DurationSeconds, bool HasAudio);

    private async Task<Probe?> ProbeAsync(string source, CancellationToken cancellationToken)
    {
        var run = await RunAsync(
            options.Value.FfprobePath,
            ["-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height", "-of", "json", source],
            cancellationToken).ConfigureAwait(false);
        if (run.ExitCode != 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(run.Output);
            var streams = document.RootElement.TryGetProperty("streams", out var list) ? list.EnumerateArray().ToList() : [];
            var picture = streams.FirstOrDefault(stream => stream.GetProperty("codec_type").GetString() == "video");
            var duration = document.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var value)
                && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;

            return picture.ValueKind == JsonValueKind.Undefined
                ? new Probe(0, 0, duration, false)
                : new Probe(
                    picture.TryGetProperty("width", out var width) ? width.GetInt32() : 0,
                    picture.TryGetProperty("height", out var height) ? height.GetInt32() : 0,
                    duration,
                    streams.Exists(stream => stream.GetProperty("codec_type").GetString() == "audio"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Run(int ExitCode, string Output);

    private static async Task<Run> RunAsync(string program, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        await errors.ConfigureAwait(false);
        return new Run(process.ExitCode, await output.ConfigureAwait(false));
    }
}
