using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Streaming.Application.Access;
using Streaming.Application.History;
using Streaming.Application.Playback.GetProgress;
using Streaming.Application.Ratings;
using Streaming.Domain.Access;
using Streaming.Domain.Ratings;
using Streaming.Infrastructure.Persistence;
using Streaming.IntegrationTests.Admin;

namespace Streaming.IntegrationTests.Taste;

/// <summary>Ratings, the watching history and the taste drawn from both, through the API.</summary>
[Collection(nameof(StreamingApiCollection))]
public sealed class TasteApiTests(StreamingApiFixture api)
{
    private static readonly JsonSerializerOptions Json = VideoApiTests.CreateJson();

    private static readonly DateTimeOffset Start = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARating_IsKept_Changed_AndTakenBack()
    {
        var (user, profile) = await ViewerAsync();
        var title = await TitleAsync();
        using var client = api.CreateClientFor(user);

        Assert.Null((await RatingAsync(client, profile, title)).Rating);

        Assert.Equal(HttpStatusCode.NoContent, (await RateAsync(client, profile, title, "Like")).StatusCode);
        Assert.Equal(RatingLevel.Like, (await RatingAsync(client, profile, title)).Rating);

        Assert.Equal(HttpStatusCode.NoContent, (await RateAsync(client, profile, title, "Love")).StatusCode);
        Assert.Equal(RatingLevel.Love, (await RatingAsync(client, profile, title)).Rating);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/profiles/{profile}/ratings/{title.Id}", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/profiles/{profile}/ratings/{title.Id}", Cancellation)).StatusCode);
        Assert.Null((await RatingAsync(client, profile, title)).Rating);
    }

    [Fact]
    public async Task OnlyATitleTheProfileCanWatch_CanBeRated_WithAKnownLevel()
    {
        var (user, profile) = await ViewerAsync(AgeRating.Age13);
        var (adult, unpublished, title) = (await TitleAsync(rating: AgeRating.Age18), await TitleAsync(playable: false), await TitleAsync());
        using var client = api.CreateClientFor(user);

        Assert.Equal("Playback.AgeRestricted", await CodeAsync(await RateAsync(client, profile, adult, "Like")));
        Assert.Equal("Playback.NotAvailable", await CodeAsync(await RateAsync(client, profile, unpublished, "Like")));
        Assert.Equal("Playback.NotAvailable", await CodeAsync(await RateAsync(client, profile, Guid.CreateVersion7(), "Like")));

        using var missing = JsonContent.Create(new { });
        var withoutLevel = await client.PutAsync($"/profiles/{profile}/ratings/{title.Id}", missing, Cancellation);
        Assert.Equal((HttpStatusCode.BadRequest, "Validation.Failed"), (withoutLevel.StatusCode, await CodeAsync(withoutLevel)));
        Assert.Equal(HttpStatusCode.BadRequest, (await RateAsync(client, profile, title, "Meh")).StatusCode);
        Assert.Null((await RatingAsync(client, profile, title)).Rating);
    }

    [Fact]
    public async Task AnotherUsersProfile_IsNotFound_AndARemovedProfileLosesItsRatings()
    {
        var (user, profile) = await ViewerAsync();
        var (stranger, _) = await ViewerAsync();
        var title = await TitleAsync();
        using var owner = api.CreateClientFor(user);
        using var other = api.CreateClientFor(stranger);
        await RateAsync(owner, profile, title, "Love");

        Assert.Equal("Playback.ProfileNotFound", await CodeAsync(await RateAsync(other, profile, title, "NotForMe")));
        Assert.Equal("Playback.ProfileNotFound", await CodeAsync(await other.GetAsync($"/profiles/{profile}/ratings/{title.Id}", Cancellation)));
        Assert.Equal("Playback.ProfileNotFound", await CodeAsync(await other.DeleteAsync($"/profiles/{profile}/ratings/{title.Id}", Cancellation)));
        Assert.Equal("Playback.ProfileNotFound", await CodeAsync(await other.GetAsync($"/profiles/{profile}/history", Cancellation)));
        Assert.Equal("Playback.ProfileNotFound", await CodeAsync(await other.DeleteAsync($"/profiles/{profile}/history/{title.Id}", Cancellation)));
        Assert.Equal("Playback.ProfileNotFound", await CodeAsync(await other.GetAsync($"/profiles/{profile}/taste", Cancellation)));
        Assert.Equal(RatingLevel.Love, (await RatingAsync(owner, profile, title)).Rating);

        await SendAsync(new ApplyProfileCommand(profile, user, null, 2));

        Assert.Equal(0, await RatingCountAsync(rating => rating.ProfileId == profile));
    }

    [Fact]
    public async Task ADeletedAccount_LosesItsRatings_AndNoOneElsesAre()
    {
        var (user, profile) = await ViewerAsync();
        var (other, othersProfile) = await ViewerAsync();
        var title = await TitleAsync();
        using (var client = api.CreateClientFor(user))
        {
            await RateAsync(client, profile, title, "Like");
        }

        using (var client = api.CreateClientFor(other))
        {
            await RateAsync(client, othersProfile, title, "Like");
        }

        await SendAsync(new ForgetUserCommand(user));

        Assert.Equal(0, await RatingCountAsync(rating => rating.UserId == user));
        Assert.Equal(1, await RatingCountAsync(rating => rating.UserId == other));
    }

    [Fact]
    public async Task History_ListsEachTitleOnce_MostRecentFirst_InPages_OnlyWhatTheProfileCanWatch()
    {
        var (user, profile) = await ViewerAsync(AgeRating.Age13);
        var (movie, series, adult, gone) = (await TitleAsync(), await TitleAsync(TitleKind.Series, episodes: 2), await TitleAsync(rating: AgeRating.Age18), await TitleAsync());
        await WatchAsync(user, profile, movie, 1);
        await WatchAsync(user, profile, series, 2, episode: 0);
        await WatchAsync(user, profile, adult, 3);
        await WatchAsync(user, profile, gone, 4);
        await WatchAsync(user, profile, series, 5, episode: 1);
        await SendAsync(new ApplyPlayableCommand(gone.Id, TitleKind.Movie, AgeRating.Age13, [], 2));
        using var client = api.CreateClientFor(user);

        var first = await HistoryAsync(client, profile, "?pageSize=1");
        var second = await HistoryAsync(client, profile, "?pageSize=1&page=2");

        Assert.Equal(2, first.TotalCount);
        Assert.Equal((series.Id, TitleKind.Series, Start.AddHours(5)), (first.Items[0].TitleId, first.Items[0].Kind, first.Items[0].WatchedAt));
        Assert.Equal(movie.Id, Assert.Single(second.Items).TitleId);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/profiles/{profile}/history?pageSize=101", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task ATitleTakenOutOfHistory_LeavesContinueWatchingAndTheTaste()
    {
        var (user, profile) = await ViewerAsync();
        var (kept, removed) = (await TitleAsync(), await TitleAsync(TitleKind.Series, episodes: 2));
        await WatchAsync(user, profile, kept, 1);
        await WatchAsync(user, profile, removed, 2, episode: 0);
        await WatchAsync(user, profile, removed, 3, episode: 1);
        using var client = api.CreateClientFor(user);
        Assert.Equal([removed.Id, kept.Id], (await ContinueWatchingAsync(client, profile)).Select(item => item.TitleId));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/profiles/{profile}/history/{removed.Id}", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/profiles/{profile}/history/{removed.Id}", Cancellation)).StatusCode);

        Assert.Equal([kept.Id], (await HistoryAsync(client, profile)).Items.Select(item => item.TitleId));
        Assert.Equal([kept.Id], (await ContinueWatchingAsync(client, profile)).Select(item => item.TitleId));
        Assert.Equal([kept.Id], (await TasteAsync(client, profile)).Like);
    }

    [Fact]
    public async Task Taste_DrawsOnRatingsAndWatching()
    {
        var (user, profile) = await ViewerAsync();
        var (loved, liked, watched, disliked, gone) = (await TitleAsync(), await TitleAsync(), await TitleAsync(), await TitleAsync(), await TitleAsync());
        await WatchAsync(user, profile, watched, 1);
        await WatchAsync(user, profile, disliked, 2);
        using var client = api.CreateClientFor(user);
        await RateAsync(client, profile, liked, "Like");
        await RateAsync(client, profile, loved, "Love");
        await RateAsync(client, profile, disliked, "NotForMe");
        await RateAsync(client, profile, gone, "Love");
        await SendAsync(new ApplyPlayableCommand(gone.Id, TitleKind.Movie, AgeRating.Age13, [], 2));

        var taste = await TasteAsync(client, profile);

        Assert.Equal([loved.Id, liked.Id, watched.Id], taste.Like);
        Assert.Equal([disliked.Id], taste.Unlike);
        Assert.Empty(taste.Seen);
    }

    private async Task<(Guid User, Guid Profile)> ViewerAsync(AgeRating rating = AgeRating.Age18)
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var (user, profile) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await SendAsync(new ApplyProfileCommand(profile, user, rating, 1));
        return (user, profile);
    }

    private async Task<TestTitle> TitleAsync(TitleKind kind = TitleKind.Movie, AgeRating rating = AgeRating.Age13, bool playable = true, int episodes = 1)
    {
        var title = Guid.CreateVersion7();
        IReadOnlyList<PlayableItem> items = playable
            ? [.. Enumerable.Range(1, episodes).Select(sequence => new PlayableItem(kind == TitleKind.Movie ? title : Guid.CreateVersion7(), Guid.CreateVersion7(), kind == TitleKind.Movie ? 0 : 100000 + sequence))]
            : [];
        await SendAsync(new ApplyPlayableCommand(title, kind, rating, items, 1));
        return new TestTitle(title, items);
    }

    /// <summary>Progress as heartbeats leave it, half way through, <paramref name="hours"/> after the start.</summary>
    private async Task WatchAsync(Guid user, Guid profile, TestTitle title, int hours, int episode = 0)
    {
        var content = title.Items[episode].ContentId;
        var at = Start.AddHours(hours);
        await using var scope = api.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "WatchProgress" ("ProfileId", "ContentId", "UserId", "TitleId", "PositionSeconds", "DurationSeconds", "Completed", "UpdatedAt")
            VALUES ({profile}, {content}, {user}, {title.Id}, {600.0}, {1200.0}, {false}, {at})
            """,
            Cancellation);
    }

    private static Task<HttpResponseMessage> RateAsync(HttpClient client, Guid profile, Guid title, string rating)
        => client.PutAsJsonAsync($"/profiles/{profile}/ratings/{title}", new { rating }, Cancellation);

    private static Task<HttpResponseMessage> RateAsync(HttpClient client, Guid profile, TestTitle title, string rating)
        => RateAsync(client, profile, title.Id, rating);

    private static Task<TitleRatingView> RatingAsync(HttpClient client, Guid profile, TestTitle title)
        => ReadAsync<TitleRatingView>(client, $"/profiles/{profile}/ratings/{title.Id}");

    private static Task<Page<HistoryItem>> HistoryAsync(HttpClient client, Guid profile, string query = "")
        => ReadAsync<Page<HistoryItem>>(client, $"/profiles/{profile}/history{query}");

    private static Task<List<ContinueWatchingItem>> ContinueWatchingAsync(HttpClient client, Guid profile)
        => ReadAsync<List<ContinueWatchingItem>>(client, $"/profiles/{profile}/continue-watching");

    private static Task<Domain.Ratings.Taste> TasteAsync(HttpClient client, Guid profile)
        => ReadAsync<Domain.Ratings.Taste>(client, $"/profiles/{profile}/taste");

    private static async Task<T> ReadAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json, Cancellation))!;
    }

    private async Task<int> RatingCountAsync(System.Linq.Expressions.Expression<Func<TitleRating, bool>> filter)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().TitleRatings.CountAsync(filter, Cancellation);
    }

    private async Task SendAsync(ICommand command)
    {
        await using var scope = api.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(command, Cancellation);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Cancellation);
        return problem?.Extensions.TryGetValue("code", out var code) == true ? code?.ToString() : $"{(int)response.StatusCode}";
    }

    private sealed record TestTitle(Guid Id, IReadOnlyList<PlayableItem> Items);
}
