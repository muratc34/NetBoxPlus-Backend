using Catalog.Domain.Content;
using Catalog.Domain.Movies;
using Catalog.Domain.Shows;

namespace Catalog.Domain.Tests;

/// <summary>What can be watched of a title, and when the streaming service is told.</summary>
public sealed class PlayabilityTests
{
    private static Movie PublishedMovie()
    {
        var movie = ReadyMovie();
        movie.Publish(TestContent.Now);
        movie.ClearDomainEvents();
        return movie;
    }

    private static Movie ReadyMovie()
    {
        var movie = TestContent.Movie();
        movie.SetGenres([Guid.CreateVersion7()]);
        movie.SetImage(ContentImageSlot.Poster, "poster.jpg");
        movie.ClearDomainEvents();
        return movie;
    }

    private static (Series Series, Episode First, Episode Second) SeriesWithTwoPlayableEpisodes()
    {
        var (series, season) = TestContent.SeriesWithSeason();
        var first = series.AddEpisode(season.Id, TestContent.Details(1)).Value;
        var second = series.AddEpisode(season.Id, TestContent.Details(2)).Value;
        TestContent.Playable(TestContent.Playable(series, first.Id), second.Id);
        series.SetGenres([Guid.CreateVersion7()]);
        series.SetImage(ContentImageSlot.Poster, "poster.jpg");
        series.ClearDomainEvents();
        return (series, first, second);
    }

    /// <summary>A video uploaded after those the test content was given, which are made with the real clock.</summary>
    private static Guid Later(int days) => Guid.CreateVersion7(DateTimeOffset.UtcNow.AddDays(days));

    private static int Announcements(ContentItem title) => title.DomainEvents.OfType<PlayabilityChangedDomainEvent>().Count();

    [Fact]
    public void PublishingAMovie_AnnouncesItsVideo()
    {
        var movie = ReadyMovie();
        var revision = movie.PlayabilityRevision;

        movie.Publish(TestContent.Now);

        Assert.Equal(1, Announcements(movie));
        var playability = movie.Playability();
        Assert.Equal(revision + 1, playability.Revision);
        Assert.Equal((movie.Id, ContentKind.Movie, AgeRating.Age13), (playability.TitleId, playability.Kind, playability.AgeRating));
        Assert.Equal([new PlayableVideo(movie.Id, movie.Video.PlayableVideoId!.Value, 0)], playability.Videos);
    }

    [Fact]
    public void ADraft_HasNothingToWatch_AndItsChangesAnnounceNothing()
    {
        var movie = ReadyMovie();

        movie.ApplyVideo(Later(1), VideoState.Ready, 2);
        movie.Describe(ContentInfo.Create(null, 2024, AgeRating.Age18).Value);

        Assert.Equal(0, Announcements(movie));
        Assert.Empty(movie.Playability().Videos);
    }

    [Fact]
    public void Unpublishing_AnnouncesAnEmptyList()
    {
        var movie = PublishedMovie();
        var revision = movie.PlayabilityRevision;

        movie.Unpublish();
        movie.Unpublish();

        Assert.Equal(1, Announcements(movie));
        Assert.Equal(revision + 1, movie.PlayabilityRevision);
        Assert.Empty(movie.Playability().Videos);
    }

    [Fact]
    public void ANewReadyVideo_OfAPublishedMovie_IsAnnounced_WithoutChangingTheVersion()
    {
        var movie = PublishedMovie();
        var version = movie.Version;
        var newer = Later(1);

        movie.ApplyVideo(newer, VideoState.Processing, 1);
        Assert.Equal(0, Announcements(movie));

        movie.ApplyVideo(newer, VideoState.Ready, 2);

        Assert.Equal(1, Announcements(movie));
        Assert.Equal(newer, Assert.Single(movie.Playability().Videos).VideoId);
        Assert.Equal(version, movie.Version);
    }

    [Fact]
    public void ANewAgeRating_OfPublishedContent_IsAnnounced_OtherEditsAreNot()
    {
        var movie = PublishedMovie();

        movie.Describe(ContentInfo.Create("Night Train", 2024, AgeRating.Age13).Value);
        Assert.Equal(0, Announcements(movie));

        movie.Describe(ContentInfo.Create("Night Train", 2024, AgeRating.Age16).Value);

        Assert.Equal(1, Announcements(movie));
        Assert.Equal(AgeRating.Age16, movie.Playability().AgeRating);
    }

    [Fact]
    public void SeveralChangesInOneSave_AreOneAnnouncement()
    {
        var movie = ReadyMovie();
        var revision = movie.PlayabilityRevision;

        movie.Publish(TestContent.Now);
        movie.Describe(ContentInfo.Create(null, 2024, AgeRating.Age7).Value);

        Assert.Equal(1, Announcements(movie));
        Assert.Equal(revision + 1, movie.PlayabilityRevision);
    }

    [Fact]
    public void ASeries_OffersItsPublishedEpisodes_OnceItIsPublished()
    {
        var (series, first, second) = SeriesWithTwoPlayableEpisodes();

        series.PublishEpisode(first.Id, TestContent.Now);
        Assert.Equal(0, Announcements(series));

        series.Publish(TestContent.Now);

        Assert.Equal(1, Announcements(series));
        var playability = series.Playability();
        Assert.Equal(ContentKind.Series, playability.Kind);
        Assert.Equal([new PlayableVideo(first.Id, first.Video.PlayableVideoId!.Value, PlayableVideo.SequenceOf(1, 1))], playability.Videos);
        Assert.DoesNotContain(playability.Videos, video => video.ContentId == second.Id);
    }

    [Fact]
    public void PublishingAndHidingEpisodes_OfAPublishedSeries_IsAnnounced()
    {
        var (series, first, second) = SeriesWithTwoPlayableEpisodes();
        series.PublishEpisode(first.Id, TestContent.Now);
        series.Publish(TestContent.Now);
        series.ClearDomainEvents();

        series.PublishEpisode(second.Id, TestContent.Now);
        Assert.Equal(1, Announcements(series));
        Assert.Equal(2, series.Playability().Videos.Count);
        series.ClearDomainEvents();

        series.PublishEpisode(second.Id, TestContent.Now);
        Assert.Equal(0, Announcements(series));

        series.UnpublishEpisode(second.Id);
        Assert.Equal(1, Announcements(series));
        Assert.Equal(first.Id, Assert.Single(series.Playability().Videos).ContentId);
    }

    [Fact]
    public void ANewVideo_OfAPublishedEpisode_IsAnnounced_OfAHiddenOneIsNot()
    {
        var (series, first, second) = SeriesWithTwoPlayableEpisodes();
        series.PublishEpisode(first.Id, TestContent.Now);
        series.Publish(TestContent.Now);
        series.ClearDomainEvents();

        series.ApplyEpisodeVideo(second.Id, Later(1), VideoState.Ready, 2);
        Assert.Equal(0, Announcements(series));

        var newer = Later(2);
        series.ApplyEpisodeVideo(first.Id, newer, VideoState.Ready, 2);

        Assert.Equal(1, Announcements(series));
        Assert.Equal(newer, Assert.Single(series.Playability().Videos).VideoId);
    }

    [Fact]
    public void Episodes_ComeInSeasonAndEpisodeOrder()
    {
        var (series, firstSeason) = TestContent.SeriesWithSeason();
        var secondSeason = series.AddSeason(2, null).Value;
        var late = series.AddEpisode(secondSeason.Id, TestContent.Details(1)).Value;
        var early = series.AddEpisode(firstSeason.Id, TestContent.Details(3)).Value;
        TestContent.Playable(TestContent.Playable(series, late.Id), early.Id);
        series.SetGenres([Guid.CreateVersion7()]);
        series.SetImage(ContentImageSlot.Poster, "poster.jpg");
        series.PublishEpisode(late.Id, TestContent.Now);
        series.PublishEpisode(early.Id, TestContent.Now);
        series.Publish(TestContent.Now);

        var videos = series.Playability().Videos;

        Assert.Equal([early.Id, late.Id], videos.Select(video => video.ContentId));
        Assert.Equal([PlayableVideo.SequenceOf(1, 3), PlayableVideo.SequenceOf(2, 1)], videos.Select(video => video.Sequence));
    }

    [Fact]
    public void RenumberingPublishedEpisodes_IsAnnounced_RenumberingHiddenOnesIsNot()
    {
        var (series, first, second) = SeriesWithTwoPlayableEpisodes();
        series.PublishEpisode(first.Id, TestContent.Now);
        series.Publish(TestContent.Now);
        var season = Assert.Single(series.Seasons);
        series.ClearDomainEvents();

        series.ChangeEpisode(second.Id, TestContent.Details(5));
        Assert.Equal(0, Announcements(series));

        series.ChangeEpisode(first.Id, TestContent.Details(4));
        Assert.Equal(1, Announcements(series));
        Assert.Equal(PlayableVideo.SequenceOf(1, 4), Assert.Single(series.Playability().Videos).Sequence);
        series.ClearDomainEvents();

        series.ChangeSeason(season.Id, 1, null);
        Assert.Equal(0, Announcements(series));
        series.ChangeSeason(season.Id, 3, null);
        Assert.Equal(1, Announcements(series));
        Assert.Equal(PlayableVideo.SequenceOf(3, 4), Assert.Single(series.Playability().Videos).Sequence);
    }
}
