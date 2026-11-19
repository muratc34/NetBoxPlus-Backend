using Catalog.Domain.Content;
using Catalog.Domain.Movies;

namespace Catalog.Domain.Tests;

public sealed class MovieTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(Runtime.MaxMinutes + 1)]
    public void Create_RejectsARuntimeOutOfRange(int minutes)
        => Assert.Equal(ContentErrors.RuntimeOutOfRange, Movie.Create(TestContent.Info(), TestContent.Texts(), minutes).Error);

    [Fact]
    public void NewMovie_IsADraft()
    {
        var movie = TestContent.Movie();

        Assert.Equal(PublicationStatus.Draft, movie.Status);
        Assert.Null(movie.PublishedAt);
    }

    [Fact]
    public void Publish_NeedsAGenre()
    {
        var movie = TestContent.Movie();

        Assert.Equal(ContentErrors.GenreRequired, movie.Publish(TestContent.Now).Error);
        Assert.False(movie.IsPublished);
    }

    [Fact]
    public void Publish_SetsTheStatusAndTime_AndAgainChangesNothing()
    {
        var movie = Published();

        Assert.True(movie.Publish(TestContent.Now.AddDays(1)).IsSuccess);

        Assert.True(movie.IsPublished);
        Assert.Equal(TestContent.Now, movie.PublishedAt);
    }

    [Fact]
    public void Unpublish_ReturnsToDraft()
    {
        var movie = Published();

        movie.Unpublish();

        Assert.Equal(PublicationStatus.Draft, movie.Status);
        Assert.Null(movie.PublishedAt);
    }

    [Fact]
    public void PublishedMovie_CannotLoseItsLastGenre()
    {
        var movie = Published();

        Assert.Equal(ContentErrors.GenreRequired, movie.SetGenres([]).Error);
        Assert.Single(movie.Genres);
    }

    [Fact]
    public void DraftMovie_CanLoseItsGenres()
    {
        var movie = TestContent.Movie();
        movie.SetGenres([Guid.CreateVersion7()]);

        Assert.True(movie.SetGenres([]).IsSuccess);
        Assert.Empty(movie.Genres);
    }

    [Fact]
    public void SetGenres_KeepsOneOfEach_AndDropsTheOthers()
    {
        var movie = TestContent.Movie();
        var kept = Guid.CreateVersion7();
        var dropped = Guid.CreateVersion7();
        movie.SetGenres([kept, dropped]);

        movie.SetGenres([kept, kept]);

        Assert.Equal(kept, Assert.Single(movie.Genres).GenreId);
    }

    [Fact]
    public void CanDelete_OnlyWhenUnpublished()
    {
        var movie = Published();

        Assert.Equal(ContentErrors.MustBeUnpublished, movie.CanDelete().Error);

        movie.Unpublish();
        Assert.True(movie.CanDelete().IsSuccess);
    }

    [Fact]
    public void SetCredits_AllowsSeveralRolesForAPerson_ButNotTheSameRoleTwice()
    {
        var movie = TestContent.Movie();
        var person = Guid.CreateVersion7();

        Assert.True(movie.SetCredits([new CreditInput(person, CreditRole.Director, null, 0), new CreditInput(person, CreditRole.Writer, null, 1)]).IsSuccess);
        Assert.Equal(2, movie.Credits.Count);

        var duplicate = movie.SetCredits([new CreditInput(person, CreditRole.Actor, "A", 0), new CreditInput(person, CreditRole.Actor, "B", 1)]);
        Assert.Equal(ContentErrors.CreditDuplicate, duplicate.Error);
        Assert.Equal(2, movie.Credits.Count);
    }

    [Fact]
    public void SetCredits_UpdatesExisting_AddsNew_AndRemovesTheRest()
    {
        var movie = TestContent.Movie();
        var kept = Guid.CreateVersion7();
        var removed = Guid.CreateVersion7();
        var added = Guid.CreateVersion7();
        movie.SetCredits([new CreditInput(kept, CreditRole.Actor, "Old", 0), new CreditInput(removed, CreditRole.Actor, null, 1)]);
        var instance = movie.Credits.First();

        movie.SetCredits([new CreditInput(kept, CreditRole.Actor, " New ", 5), new CreditInput(added, CreditRole.Director, null, 6)]);

        Assert.Equal(2, movie.Credits.Count);
        Assert.Same(instance, movie.Credits.Single(credit => credit.PersonId == kept));
        Assert.Equal("New", instance.CharacterName);
        Assert.Equal(5, instance.Order);
        Assert.Contains(movie.Credits, credit => credit.PersonId == added);
    }

    [Fact]
    public void SetCredits_RejectsAnUnknownRole_AndATooLongCharacterName()
    {
        var movie = TestContent.Movie();
        var longName = new string('a', Credit.CharacterNameMaxLength + 1);

        Assert.Equal(ContentErrors.CreditRoleInvalid, movie.SetCredits([new CreditInput(Guid.CreateVersion7(), (CreditRole)99, null, 0)]).Error);
        Assert.Equal(ContentErrors.CharacterNameTooLong, movie.SetCredits([new CreditInput(Guid.CreateVersion7(), CreditRole.Actor, longName, 0)]).Error);
        Assert.Empty(movie.Credits);
    }

    [Fact]
    public void Version_GrowsWithEveryChange_ButNotWithARefusedOne()
    {
        var movie = TestContent.Movie();
        var versions = new List<int> { movie.Version };

        movie.Describe(TestContent.Info("Other"));
        versions.Add(movie.Version);
        movie.ChangeRuntime(90);
        versions.Add(movie.Version);
        movie.SetGenres([Guid.CreateVersion7()]);
        versions.Add(movie.Version);
        movie.SetCredits([]);
        versions.Add(movie.Version);
        movie.SetImage(ContentImageSlot.Poster, "poster.jpg");
        versions.Add(movie.Version);
        movie.Publish(TestContent.Now);
        versions.Add(movie.Version);
        movie.Publish(TestContent.Now);
        Assert.Equal(versions[^1], movie.Version);
        movie.Unpublish();
        versions.Add(movie.Version);

        Assert.Equal(versions.Count, versions.Distinct().Count());
        Assert.True(movie.ChangeRuntime(0).IsFailure);
        Assert.Equal(versions[^1], movie.Version);
    }

    [Fact]
    public void CheckVersion_RefusesAStaleCopy()
    {
        var movie = TestContent.Movie();

        Assert.True(movie.CheckVersion(movie.Version).IsSuccess);
        Assert.Equal(ContentErrors.VersionConflict, movie.CheckVersion(movie.Version + 1).Error);
    }

    [Fact]
    public void Publish_NeedsAPoster()
    {
        var movie = TestContent.Movie();
        movie.SetGenres([Guid.CreateVersion7()]);

        Assert.Equal(ContentErrors.PosterRequired, movie.Publish(TestContent.Now).Error);

        movie.SetImage(ContentImageSlot.Poster, "poster.jpg");
        Assert.True(movie.Publish(TestContent.Now).IsSuccess);
    }

    [Fact]
    public void PublishedMovie_KeepsItsPoster_ButCanLoseItsBackdrop()
    {
        var movie = Published();
        movie.SetImage(ContentImageSlot.Backdrop, "backdrop.jpg");

        Assert.Equal(ContentErrors.PosterRequired, movie.RemoveImage(ContentImageSlot.Poster).Error);
        Assert.Equal("poster.jpg", movie.PosterImage);
        Assert.True(movie.RemoveImage(ContentImageSlot.Backdrop).IsSuccess);
        Assert.Null(movie.BackdropImage);
    }

    [Fact]
    public void ImageChanges_RaiseTheVersion()
    {
        var movie = TestContent.Movie();
        var before = movie.Version;

        movie.SetImage(ContentImageSlot.Backdrop, "backdrop.jpg");
        var afterSet = movie.Version;
        movie.RemoveImage(ContentImageSlot.Backdrop);

        Assert.True(before < afterSet && afterSet < movie.Version);
    }

    private static Movie Published()
    {
        var movie = TestContent.Movie();
        movie.SetGenres([Guid.CreateVersion7()]);
        movie.SetImage(ContentImageSlot.Poster, "poster.jpg");
        movie.Publish(TestContent.Now);
        return movie;
    }
}
