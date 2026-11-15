namespace NetBoxPlus.DemoSeeder;

/// <summary>Bound from the <c>Seeder</c> section; in containers from <c>Seeder__*</c> environment variables.</summary>
internal sealed class SeederOptions
{
    public const string SectionName = "Seeder";

    /// <summary>The gateway, the only address the seeder talks to besides blob storage and Stripe.</summary>
    public Uri Gateway { get; set; } = new("http://localhost:5100");

    public string AdminEmail { get; set; } = "";

    public string AdminPassword { get; set; } = "";

    /// <summary>
    /// Blob storage as the services hand it out (to browsers), and where this process reaches the
    /// same storage instead. Inside the compose network the first is <c>http://127.0.0.1:10000</c>,
    /// which is the container itself, so it is replaced with <c>http://azurite:10000</c>. Both unset: no change.
    /// </summary>
    public Uri? PublicStorageEndpoint { get; set; }

    public Uri? StorageEndpoint { get; set; }

    /// <summary>Stripe test-mode secret key. Without it the subscription plans are skipped.</summary>
    public string? StripeSecretKey { get; set; }

    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>A TrueType font for the titles drawn onto images and videos.</summary>
    public string FontFile { get; set; } = OperatingSystem.IsWindows()
        ? @"C:\Windows\Fonts\arialbd.ttf"
        : "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf";

    /// <summary>Where generated media is kept; reused when the seeder runs again on the same machine.</summary>
    public string WorkDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "netboxplus-demo-seed");

    /// <summary>How long to wait for all videos to be transcoded.</summary>
    public TimeSpan TranscodeTimeout { get; set; } = TimeSpan.FromMinutes(20);
}
