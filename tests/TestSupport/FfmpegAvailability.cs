namespace NetBoxPlus.Testing;

/// <summary>
/// Finds ffmpeg and ffprobe for tests that convert real video. Like Docker: locally a missing ffmpeg
/// skips those tests; where <see cref="DockerAvailability.RequireVariable"/> is set (CI) it fails them.
/// </summary>
internal static class FfmpegAvailability
{
    /// <summary>A folder holding ffmpeg and ffprobe, when they are not on the PATH.</summary>
    public const string DirectoryVariable = "FFMPEG_DIRECTORY";

    public static string? FfmpegPath { get; } = Find("ffmpeg");

    public static string? FfprobePath { get; } = Find("ffprobe");

    public static bool IsAvailable => FfmpegPath is not null && FfprobePath is not null;

    public static void SkipUnlessAvailable()
    {
        if (IsAvailable)
        {
            return;
        }

        if (string.Equals(Environment.GetEnvironmentVariable(DockerAvailability.RequireVariable), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail("ffmpeg and ffprobe are required here but were not found on the PATH.");
        }

        Assert.Skip($"ffmpeg is not installed; put it on the PATH or set {DirectoryVariable}.");
    }

    private static string? Find(string program)
    {
        var name = OperatingSystem.IsWindows() ? program + ".exe" : program;
        var folders = new[] { Environment.GetEnvironmentVariable(DirectoryVariable) }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator));

        return folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => Path.Combine(folder!.Trim(), name))
            .FirstOrDefault(File.Exists);
    }
}
