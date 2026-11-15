using System.Buffers.Binary;
using NetBoxPlus.DemoSeeder.Content;
using NetBoxPlus.DemoSeeder.Media;
using NetBoxPlus.Testing;

namespace NetBoxPlus.DemoSeeder.Tests;

/// <summary>The generated images must pass the catalog's image rules: poster at least 300×450 at 2:3, backdrop 1280×720 at 16:9.</summary>
public sealed class MediaFactoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "netboxplus-seeder-tests", Guid.NewGuid().ToString("N"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Images_HaveTheSizesTheCatalogAccepts()
    {
        var media = Factory();

        Assert.Equal((600, 900), PngSize(await media.PosterAsync("Rüzgarın Kızı: 'Fırtına'", new Palette("navy", "teal"), Cancellation)));
        Assert.Equal((1280, 720), PngSize(await media.BackdropAsync("Kuzey Işığı", new Palette("navy", "teal"), Cancellation)));
        Assert.Equal((400, 400), PngSize(await media.PhotoAsync("Defne Aksoy", Cancellation)));
    }

    [Fact]
    public async Task AVideo_IsMadeOnce_AndReused()
    {
        var media = Factory();

        var first = await media.VideoAsync("Last Stop Mars", 720, Cancellation);
        var written = File.GetLastWriteTimeUtc(first);
        var second = await media.VideoAsync("Last Stop Mars", 720, Cancellation);

        Assert.Equal(first, second);
        Assert.Equal(written, File.GetLastWriteTimeUtc(second));
        Assert.True(new FileInfo(first).Length > 100_000);
        Assert.NotEqual(first, await media.VideoAsync("Last Stop Mars", 1080, Cancellation));
    }

    [Fact]
    public async Task AFailingFfmpeg_Fails_AndLeavesNoImageToReuse()
    {
        FfmpegAvailability.SkipUnlessAvailable();
        var media = new MediaFactory(new SeederOptions { FfmpegPath = FfmpegAvailability.FfmpegPath!, WorkDirectory = _folder });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => media.PosterAsync("Kuzey Işığı", new Palette("not-a-colour", "teal"), Cancellation));
        Assert.StartsWith("ffmpeg exited with", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Directory.EnumerateFiles(_folder), file => file.EndsWith(".png", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Kuzey Işığı", "Kuzey Işığı")]
    [InlineData("Daughter of the Wind", "Daughter of\nthe Wind")]
    [InlineData("Supercalifragilistic", "Supercalifragilistic")]
    public void LongTitles_GoOnTwoLines(string title, string expected)
        => Assert.Equal(expected, MediaFactory.Wrap(title));

    [Fact]
    public void FilterPaths_EscapeTheDriveColon()
        => Assert.Equal("C\\:/Windows/Fonts/arialbd.ttf", MediaFactory.FilterPath(@"C:\Windows\Fonts\arialbd.ttf"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private MediaFactory Factory()
    {
        FfmpegAvailability.SkipUnlessAvailable();
        var options = new SeederOptions { FfmpegPath = FfmpegAvailability.FfmpegPath!, WorkDirectory = _folder };
        Assert.True(File.Exists(options.FontFile), $"The font {options.FontFile} is missing; on Linux install fonts-dejavu-core.");
        return new MediaFactory(options);
    }

    // Width and height are the first fields of the IHDR chunk, right after the 8-byte signature and the chunk header.
    private static (int Width, int Height) PngSize(string file)
    {
        var header = File.ReadAllBytes(file).AsSpan(0, 24);
        return (BinaryPrimitives.ReadInt32BigEndian(header[16..]), BinaryPrimitives.ReadInt32BigEndian(header[20..]));
    }
}
