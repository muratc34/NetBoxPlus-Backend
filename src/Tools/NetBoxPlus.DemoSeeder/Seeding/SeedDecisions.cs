using NetBoxPlus.DemoSeeder.Gateway;

namespace NetBoxPlus.DemoSeeder.Seeding;

/// <summary>What to do about a title's video, given the videos the streaming service already has for it.</summary>
internal abstract record VideoStep
{
    private VideoStep()
    {
    }

    /// <summary>A video is ready; nothing to upload or wait for.</summary>
    public sealed record Ready(Guid VideoId) : VideoStep;

    /// <summary>A video was uploaded and is being transcoded; wait for it.</summary>
    public sealed record Wait(Guid VideoId) : VideoStep;

    /// <summary>No usable video: none yet, or the last one failed, expired or was never uploaded.</summary>
    public sealed record Upload : VideoStep;
}

/// <summary>What to do about one language of a dubbed demo movie.</summary>
internal enum TrackStep
{
    /// <summary>It is there, or being converted.</summary>
    None,

    Upload,

    /// <summary>An upload left waiting by an earlier run: its address is gone, so it is removed and made again.</summary>
    ReplaceAndUpload,
}

/// <summary>What a dubbed demo movie's ready video still needs.</summary>
/// <param name="ConvertAgain">The video was converted before audio tracks: a dub needs it uploaded again.</param>
internal sealed record LanguagePlan(bool ConvertAgain, TrackStep Dub, IReadOnlyDictionary<string, TrackStep> Subtitles);

/// <summary>The choices that make a second run continue where the first stopped, or do nothing.</summary>
internal static class SeedDecisions
{
    public static VideoStep ForVideo(IEnumerable<VideoItem> videos)
    {
        // The latest one decides: it is the one the catalog follows. An upload left waiting by an
        // earlier run cannot be finished, since its address is gone, so it is started again.
        var latest = videos.MaxBy(video => video.CreatedAt);
        return latest?.Status switch
        {
            "Ready" => new VideoStep.Ready(latest.Id),
            "Queued" => new VideoStep.Wait(latest.Id),
            _ => new VideoStep.Upload(),
        };
    }

    public const string DubLanguage = "en-US";

    public static readonly IReadOnlyList<string> SubtitleLanguages = ["tr-TR", "en-US"];

    public static LanguagePlan ForLanguages(VideoDetails video)
    {
        if (video.AudioLayout != "Separate")
        {
            return new LanguagePlan(ConvertAgain: true, TrackStep.None, new Dictionary<string, TrackStep>());
        }

        return new LanguagePlan(
            ConvertAgain: false,
            StepFor(video.AudioTracks, DubLanguage),
            SubtitleLanguages.ToDictionary(language => language, language => StepFor(video.Subtitles, language)));
    }

    private static TrackStep StepFor(IReadOnlyList<TrackItem>? tracks, string language)
        => tracks?.FirstOrDefault(track => track.Language == language)?.Status switch
        {
            "Ready" or "Queued" => TrackStep.None,
            "AwaitingUpload" => TrackStep.ReplaceAndUpload,
            _ => TrackStep.Upload,
        };

    public static bool NeedsPublishing(string status) => !string.Equals(status, Statuses.Published, StringComparison.Ordinal);

    public static bool NeedsImage(string? currentAddress) => string.IsNullOrEmpty(currentAddress);

    /// <summary>A Stripe key the seeder may use: test mode only, since it creates products and prices.</summary>
    public static bool IsTestModeKey(string key)
        => key.StartsWith("sk_test_", StringComparison.Ordinal) || key.StartsWith("rk_test_", StringComparison.Ordinal);
}
