using Catalog.Domain.Content;
using Catalog.Domain.Movies;

namespace Catalog.Domain.Tests;

/// <remarks>Ids carry explicit times: version 7 Guids made in the same millisecond are in no particular order.</remarks>
public sealed class VideoLanguageTests
{
    private static readonly Guid Older = Guid.CreateVersion7(TestContent.Now);
    private static readonly Guid Newer = Guid.CreateVersion7(TestContent.Now.AddMinutes(1));

    private static Movie Movie() => Catalog.Domain.Movies.Movie.Create(TestContent.Info(), TestContent.Texts(), 120).Value;

    [Fact]
    public void ThePlayableVideosLanguages_AreShown_InEitherOrderOfArrival()
    {
        var first = Movie();
        first.ApplyVideo(Older, VideoState.Ready, 2);
        first.ApplyVideoTracks(Older, ["tr-TR", "en-US"], ["en-US"], 3);

        var second = Movie();
        second.ApplyVideoTracks(Older, ["tr-TR", "en-US"], ["en-US"], 3);
        Assert.Empty(second.Video.AudioLanguages);
        second.ApplyVideo(Older, VideoState.Ready, 2);

        foreach (var movie in new[] { first, second })
        {
            Assert.Equal(["tr-TR", "en-US"], movie.Video.AudioLanguages);
            Assert.Equal(["en-US"], movie.Video.SubtitleLanguages);
        }
    }

    [Fact]
    public void AnOlderReport_ChangesNothing()
    {
        var movie = Movie();
        movie.ApplyVideo(Older, VideoState.Ready, 2);
        movie.ApplyVideoTracks(Older, ["tr-TR"], ["tr-TR", "en-US"], 5);

        movie.ApplyVideoTracks(Older, ["tr-TR", "de-DE"], [], 4);
        movie.ApplyVideoTracks(Older, ["tr-TR", "de-DE"], [], 5);

        Assert.Equal(["tr-TR"], movie.Video.AudioLanguages);
        Assert.Equal(["tr-TR", "en-US"], movie.Video.SubtitleLanguages);
    }

    [Fact]
    public void ANewerVideosLanguages_WaitUntilItIsPlayable_AndAnOlderOnesNeverReturn()
    {
        var movie = Movie();
        movie.ApplyVideo(Older, VideoState.Ready, 2);
        movie.ApplyVideoTracks(Older, ["tr-TR"], [], 3);

        movie.ApplyVideoTracks(Newer, ["en-GB"], ["en-GB"], 3);
        Assert.Empty(movie.Video.AudioLanguages);

        movie.ApplyVideoTracks(Older, ["tr-TR", "fr-FR"], [], 9);
        movie.ApplyVideo(Newer, VideoState.Ready, 2);

        Assert.Equal(["en-GB"], movie.Video.AudioLanguages);
        Assert.Equal(["en-GB"], movie.Video.SubtitleLanguages);
    }

    [Fact]
    public void AVideoWithoutLanguages_ShowsNone()
    {
        var movie = Movie();
        movie.ApplyVideo(Older, VideoState.Ready, 2);
        movie.ApplyVideoTracks(Older, [], [], 3);

        Assert.Empty(movie.Video.AudioLanguages);
        Assert.Empty(movie.Video.SubtitleLanguages);
    }
}
