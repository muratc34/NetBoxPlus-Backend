using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;
using Shared.Application.Cqrs;
using Streaming.Application.Popularity;
using Streaming.Contracts;
using Streaming.Domain.Playback;
using Streaming.Domain.Popularity;
using Streaming.Infrastructure.Persistence;
using Streaming.IntegrationTests.Admin;

namespace Streaming.IntegrationTests.Popularity;

/// <summary>
/// The ranking computed from sessions in the database. Each test starts from no ranking: the counted
/// days belong to the ranking, and sessions placed in the past are only counted on a day not yet counted.
/// </summary>
[Collection(nameof(StreamingApiCollection))]
public sealed class PopularityTests(StreamingApiFixture api)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AProfile_CountsOnceADay_OverTheLastSevenDays()
    {
        await ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var (title, older, outside) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var (first, second, third) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await PlayAsync(title, first, now);
        await PlayAsync(title, first, now);
        await PlayAsync(title, second, now);
        await PlayAsync(title, first, now.AddDays(-1));
        await PlayAsync(title, first, now.AddDays(-1));
        await PlayAsync(older, first, now.AddDays(-6));
        await PlayAsync(older, second, now.AddDays(-6));
        await PlayAsync(outside, first, now.AddDays(-7));
        await PlayAsync(outside, second, now.AddDays(-7));
        await PlayAsync(outside, third, now.AddDays(-7));
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        await using (var setup = api.CreateScope())
        {
            // Counted when it was still in the window; now it is a day too old.
            var context = setup.ServiceProvider.GetRequiredService<StreamingDbContext>();
            context.DailyPlays.Add(new DailyPlays(outside, today.AddDays(-7), 5));
            await context.SaveChangesAsync(Cancellation);
        }

        await ComputeAsync();

        var scores = await ScoresAsync();
        Assert.Equal(3, scores.GetValueOrDefault(title));
        Assert.Equal(2, scores.GetValueOrDefault(older));
        Assert.False(scores.ContainsKey(outside));

        await using var scope = api.CreateScope();
        var counted = await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().DailyPlays
            .Where(plays => plays.TitleId == title || plays.TitleId == older || plays.TitleId == outside)
            .OrderBy(plays => plays.Day)
            .Select(plays => new { plays.TitleId, plays.Day, plays.Plays })
            .ToListAsync(Cancellation);
        Assert.Equal([new { TitleId = older, Day = today.AddDays(-6), Plays = 2 }, new { TitleId = title, Day = today.AddDays(-1), Plays = 1 }], counted);
    }

    [Fact]
    public async Task AFinishedDay_IsKept_AfterItsSessionsAreCleanedUp_AndNotCountedTwice()
    {
        await ResetAsync();
        var title = Guid.CreateVersion7();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);
        await PlayAsync(title, Guid.CreateVersion7(), yesterday);
        await PlayAsync(title, Guid.CreateVersion7(), yesterday);
        await ComputeAsync();

        await using (var scope = api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().PlaybackSessions
                .Where(session => session.TitleId == title)
                .ExecuteDeleteAsync(Cancellation);
        }

        await ComputeAsync();
        await ComputeAsync();

        Assert.Equal(2, (await ScoresAsync()).GetValueOrDefault(title));
    }

    [Fact]
    public async Task AChangedRanking_IsPublished_AndAnUnchangedOneIsNot()
    {
        await ResetAsync();
        var title = Guid.CreateVersion7();
        await PlayAsync(title, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        var before = await PublishedAsync();

        await ComputeAsync();
        var first = Assert.Single(await PublishedAsync(), message => !before.Contains(message));
        var update = JsonSerializer.Deserialize<PopularityUpdated>(first, Json)!;
        Assert.Contains(new Contracts.PopularTitle(title, 1), update.Titles);
        Assert.Equal(update.Titles.OrderByDescending(entry => entry.Score), update.Titles);

        await ComputeAsync();
        Assert.Equal(before.Count + 1, (await PublishedAsync()).Count);

        await PlayAsync(title, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        await ComputeAsync();
        var published = await PublishedAsync();
        Assert.Equal(before.Count + 2, published.Count);
        var next = JsonSerializer.Deserialize<PopularityUpdated>(Assert.Single(published, message => !before.Contains(message) && message != first), Json)!;
        Assert.Equal(update.Revision + 1, next.Revision);
        Assert.Contains(new Contracts.PopularTitle(title, 2), next.Titles);
    }

    [Fact]
    public async Task ARecentComputation_IsNotRepeated()
    {
        await ResetAsync();
        await ComputeAsync();
        var title = Guid.CreateVersion7();
        await PlayAsync(title, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        await ComputeAsync(TimeSpan.FromHours(1));
        Assert.False((await ScoresAsync()).ContainsKey(title));

        await ComputeAsync();
        Assert.True((await ScoresAsync()).ContainsKey(title));
    }

    [Fact]
    public async Task OfTwoComputationsAtOnce_OnlyOneIsSaved()
    {
        await ResetAsync();
        await ComputeAsync();

        await using var late = api.CreateScope();
        var ranking = (await late.ServiceProvider.GetRequiredService<IPopularityRepository>().GetAsync(Cancellation))!;
        ranking.Update([new TitleScore(Guid.CreateVersion7(), 1)], DateOnly.FromDateTime(DateTime.UtcNow), DateTimeOffset.UtcNow);

        await ComputeAsync();

        await Assert.ThrowsAsync<DataConflictException>(() => late.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Cancellation));
    }

    private async Task ResetAsync()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
        await context.PopularityRankings.ExecuteDeleteAsync(Cancellation);
        await context.DailyPlays.ExecuteDeleteAsync(Cancellation);
    }

    /// <summary>A session the profile started at the time, on a screen of its own.</summary>
    private async Task PlayAsync(Guid title, Guid profile, DateTimeOffset at)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
        var viewer = Viewer.Create(Guid.CreateVersion7());
        Assert.True(viewer.Start(new PlaybackGrant(profile, Guid.CreateVersion7(), title, Guid.CreateVersion7(), 1080), 1, at).IsSuccess);
        context.Viewers.Add(viewer);
        await context.SaveChangesAsync(Cancellation);
    }

    private async Task ComputeAsync(TimeSpan minimumGap = default)
    {
        await using var scope = api.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new ComputePopularityCommand(minimumGap), Cancellation);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
    }

    private async Task<Dictionary<Guid, int>> ScoresAsync()
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().Set<Domain.Popularity.PopularTitle>()
            .ToDictionaryAsync(title => title.TitleId, title => title.Score, Cancellation);
    }

    private async Task<List<string>> PublishedAsync()
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().OutboxMessages
            .Where(message => message.Type == "Streaming.Contracts." + nameof(PopularityUpdated))
            .Select(message => message.Payload)
            .ToListAsync(Cancellation);
    }
}
