using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NetBoxPlus.DemoSeeder.Content;

namespace NetBoxPlus.DemoSeeder.Media;

/// <summary>
/// Makes the demo images and videos with ffmpeg: colour gradients and test patterns with the title
/// drawn on, so nothing copyrighted is needed. A file already made with the same inputs is reused.
/// </summary>
internal sealed class MediaFactory(SeederOptions options)
{
    public const int VideoSeconds = 30;

    public Task<string> PosterAsync(string title, Palette palette, CancellationToken cancellationToken)
        => ImageAsync("poster", title, palette, 600, 900, 64, cancellationToken);

    public Task<string> BackdropAsync(string title, Palette palette, CancellationToken cancellationToken)
        => ImageAsync("backdrop", title, palette, 1280, 720, 72, cancellationToken);

    public Task<string> PhotoAsync(string name, CancellationToken cancellationToken)
        => ImageAsync("photo", name, new Palette("slategray", "lightsteelblue"), 400, 400, 36, cancellationToken);

    /// <summary>A test pattern with the title, a running clock and a tone.</summary>
    public async Task<string> VideoAsync(string title, int height, CancellationToken cancellationToken)
    {
        var width = height * 16 / 9;
        var file = OutputFile("video", $"{title}|{height}", ".mp4");
        if (File.Exists(file))
        {
            return file;
        }

        var titleFile = await TextFileAsync(file, title, cancellationToken).ConfigureAwait(false);
        var font = FilterPath(options.FontFile);
        var size = height / 12;
        var filter =
            $"drawtext=fontfile='{font}':textfile='{FilterPath(titleFile)}':fontsize={size}:fontcolor=white:borderw=3:bordercolor=black:x=(w-text_w)/2:y=h/6," +
            $"drawtext=fontfile='{font}':text='%{{pts\\:hms}}':fontsize={size}:fontcolor=white:borderw=3:bordercolor=black:x=(w-text_w)/2:y=h-h/4";
        await RunAsync(
            [
                "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate=25:duration={VideoSeconds}",
                "-f", "lavfi", "-i", $"sine=frequency=440:duration={VideoSeconds}",
                "-vf", filter,
                "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "96k", "-shortest",
            ],
            file,
            cancellationToken).ConfigureAwait(false);
        return file;
    }

    /// <summary>A dub as long as the videos: a tone of another pitch, so that switching is heard.</summary>
    public async Task<string> DubAsync(CancellationToken cancellationToken)
    {
        var file = OutputFile("dub", "660", ".m4a");
        if (!File.Exists(file))
        {
            await RunAsync(["-f", "lavfi", "-i", $"sine=frequency=660:duration={VideoSeconds}", "-c:a", "aac", "-b:a", "96k"], file, cancellationToken).ConfigureAwait(false);
        }

        return file;
    }

    /// <summary>Subtitles in SRT, one line every five seconds of the video, naming the language and the time.</summary>
    public async Task<string> SubtitlesAsync(string language, CancellationToken cancellationToken)
    {
        var file = OutputFile("subtitles", language, ".srt");
        var text = new StringBuilder();
        for (var cue = 0; cue < VideoSeconds / 5; cue++)
        {
            var start = TimeSpan.FromSeconds(cue * 5);
            var end = start + TimeSpan.FromSeconds(4);
            text.Append(CultureInfo.InvariantCulture, $"{cue + 1}\n{start:hh\\:mm\\:ss},000 --> {end:hh\\:mm\\:ss},000\n{language}: {start:mm\\:ss}\n\n");
        }

        await File.WriteAllTextAsync(file, text.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return file;
    }

    private async Task<string> ImageAsync(string kind, string title, Palette palette, int width, int height, int fontSize, CancellationToken cancellationToken)
    {
        var file = OutputFile(kind, $"{title}|{palette}|{width}x{height}", ".png");
        if (File.Exists(file))
        {
            return file;
        }

        var titleFile = await TextFileAsync(file, Wrap(title), cancellationToken).ConfigureAwait(false);
        var filter =
            $"drawtext=fontfile='{FilterPath(options.FontFile)}':textfile='{FilterPath(titleFile)}':fontsize={fontSize}:fontcolor=white:" +
            "borderw=2:bordercolor=black@0.6:line_spacing=12:x=(w-text_w)/2:y=(h-text_h)/2";
        await RunAsync(
            [
                "-f", "lavfi", "-i", $"gradients=size={width}x{height}:c0={palette.From}:c1={palette.To}:nb_colors=2:duration=1",
                "-vf", filter,
                "-frames:v", "1",
            ],
            file,
            cancellationToken).ConfigureAwait(false);
        return file;
    }

    /// <summary>Long titles go on two lines so they fit a poster.</summary>
    internal static string Wrap(string title)
    {
        const int lineLength = 14;
        if (title.Length <= lineLength || !title.Contains(' ', StringComparison.Ordinal))
        {
            return title;
        }

        var middle = title.Length / 2;
        var before = title.LastIndexOf(' ', middle);
        var after = title.IndexOf(' ', middle);
        var split = before < 0 ? after : after < 0 ? before : middle - before <= after - middle ? before : after;
        return title[..split] + "\n" + title[(split + 1)..];
    }

    /// <summary>
    /// A path inside an ffmpeg filter, which treats ':' and '\' as special: forward slashes, and the
    /// drive colon escaped.
    /// </summary>
    internal static string FilterPath(string path) => path.Replace('\\', '/').Replace(":", "\\:", StringComparison.Ordinal);

    private string OutputFile(string kind, string inputs, string extension)
    {
        Directory.CreateDirectory(options.WorkDirectory);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inputs)))[..16];
        return Path.Combine(options.WorkDirectory, $"{kind}-{hash}{extension}");
    }

    // The text goes through a file: titles have quotes, colons and non-ASCII letters that the filter
    // syntax would need escaped.
    private static async Task<string> TextFileAsync(string forFile, string text, CancellationToken cancellationToken)
    {
        var path = forFile + ".txt";
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return path;
    }

    private async Task RunAsync(IReadOnlyList<string> arguments, string output, CancellationToken cancellationToken)
    {
        var partial = output + ".partial" + Path.GetExtension(output);
        var start = new ProcessStartInfo(options.FfmpegPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in (string[])["-hide_banner", "-loglevel", "error", "-y", .. arguments, partial])
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {options.FfmpegPath}.");
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var message = (await errors.ConfigureAwait(false)).Trim();
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"ffmpeg exited with {process.ExitCode} making {Path.GetFileName(output)}: {message}"));
        }

        File.Move(partial, output, overwrite: true);
    }
}
