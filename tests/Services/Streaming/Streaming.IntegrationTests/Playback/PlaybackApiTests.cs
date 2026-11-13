using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;
using Streaming.Application.Abstractions;
using Streaming.Application.Access;
using Streaming.Application.Playback.StartPlayback;
using Streaming.Domain.Access;
using Streaming.Domain.Videos;
using Streaming.Infrastructure.Media;
using Streaming.Infrastructure.Persistence;
using Streaming.IntegrationTests.Admin;

namespace Streaming.IntegrationTests.Playback;

/// <summary>Watching, end to end inside the service: the rules, the screens, the signed playlists and the files.</summary>
[Collection(nameof(StreamingApiCollection))]
public sealed class PlaybackApiTests(StreamingApiFixture api)
{
    private static readonly JsonSerializerOptions Json = VideoApiTests.CreateJson();
    private static readonly byte[] Segment = Encoding.ASCII.GetBytes("not really a transport stream");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APlayer_GetsThePlansRenditions_AndItsSegmentsFromStorage()
    {
        var setup = await SetUpAsync(VideoQuality.Hd, screens: 1);
        using var client = api.CreateClientFor(setup.User);

        var response = await client.PostAsJsonAsync("/playback-sessions", new { setup.ProfileId, setup.ContentId }, Json, Cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var started = (await response.Content.ReadFromJsonAsync<PlaybackStarted>(Json, Cancellation))!;
        Assert.Equal(30, started.HeartbeatSeconds);
        Assert.StartsWith($"playback/{started.SessionId}/master.m3u8?exp=", started.Playlist, StringComparison.Ordinal);

        using var player = api.CreateAnonymousClient();
        var masterAddress = new Uri(new Uri(player.BaseAddress!, "/playback-sessions"), started.Playlist);
        var master = await player.GetAsync(masterAddress, Cancellation);
        Assert.Equal("application/vnd.apple.mpegurl", master.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", master.Headers.CacheControl?.ToString());
        var masterText = await master.Content.ReadAsStringAsync(Cancellation);
        Assert.StartsWith("#EXTM3U", masterText, StringComparison.Ordinal);
        Assert.Contains("RESOLUTION=1280x720", masterText, StringComparison.Ordinal);
        Assert.Contains("RESOLUTION=1920x1080", masterText, StringComparison.Ordinal);
        Assert.DoesNotContain("2160", masterText, StringComparison.Ordinal);

        var renditionLine = masterText.Split('\n').First(line => line.StartsWith("720p/index.m3u8?exp=", StringComparison.Ordinal));
        var rendition = await player.GetStringAsync(new Uri(masterAddress, renditionLine), Cancellation);
        var segmentLine = rendition.Split('\n').Single(line => line.Length > 0 && !line.StartsWith('#'));
        Assert.Contains("#EXT-X-ENDLIST", rendition, StringComparison.Ordinal);
        Assert.Contains($"/videos/{setup.VideoId:N}/720p/seg_0000.ts?", segmentLine, StringComparison.Ordinal);
        Assert.Contains("sp=r", segmentLine, StringComparison.Ordinal);

        using var storage = new HttpClient();
        Assert.Equal(Segment, await storage.GetByteArrayAsync(segmentLine, Cancellation));
        var unsigned = await storage.GetAsync(segmentLine[..segmentLine.IndexOf('?', StringComparison.Ordinal)], Cancellation);
        Assert.False(unsigned.IsSuccessStatusCode);
    }

    [Fact]
    public async Task AnSdPlan_GetsNothingTallerThan480Lines()
    {
        var setup = await SetUpAsync(VideoQuality.Sd, screens: 1);
        using var client = api.CreateClientFor(setup.User);
        var started = await StartAsync(client, setup);

        using var player = api.CreateAnonymousClient();
        var master = await player.GetStringAsync(new Uri(new Uri(player.BaseAddress!, "/playback-sessions"), started.Playlist), Cancellation);

        Assert.Contains("360p/index.m3u8", master, StringComparison.Ordinal);
        Assert.DoesNotContain("720p", master, StringComparison.Ordinal);

        // Even with a valid signature, a rendition above the plan is not served.
        string signed;
        await using (var scope = api.CreateScope())
        {
            signed = scope.ServiceProvider.GetRequiredService<IPlaybackSigner>().Sign(started.SessionId, "720p/index.m3u8", DateTimeOffset.UtcNow.AddMinutes(5));
        }

        Assert.Equal("Playback.NotAvailable", await CodeAsync(await player.GetAsync($"/playback/{started.SessionId}/720p/index.m3u8?{signed}", Cancellation)));
    }

    [Fact]
    public async Task Playlists_NeedTheirOwnValidSignature()
    {
        var setup = await SetUpAsync(VideoQuality.Hd, screens: 2);
        using var client = api.CreateClientFor(setup.User);
        var started = await StartAsync(client, setup);
        var other = await StartAsync(client, setup);
        using var player = api.CreateAnonymousClient();
        var query = started.Playlist[(started.Playlist.IndexOf('?', StringComparison.Ordinal) + 1)..];

        async Task<string?> MasterAsync(Guid session, string? queryString)
            => await CodeAsync(await player.GetAsync($"/playback/{session}/master.m3u8{(queryString is null ? string.Empty : "?" + queryString)}", Cancellation));

        Assert.Null(await MasterAsync(started.SessionId, query));
        Assert.Equal("Playback.InvalidSignature", await MasterAsync(started.SessionId, null));
        Assert.Equal("Playback.InvalidSignature", await MasterAsync(other.SessionId, query));
        Assert.Equal("Playback.InvalidSignature", await MasterAsync(started.SessionId, query.Replace("sig=", "sig=x", StringComparison.Ordinal)));
        Assert.Equal("Playback.InvalidSignature", await MasterAsync(started.SessionId, query.Replace("exp=", "exp=1", StringComparison.Ordinal)));
        Assert.Equal("Playback.InvalidSignature", await CodeAsync(await player.GetAsync($"/playback/{started.SessionId}/720p/index.m3u8?{query}", Cancellation)));

        string expired;
        await using (var scope = api.CreateScope())
        {
            expired = scope.ServiceProvider.GetRequiredService<IPlaybackSigner>().Sign(started.SessionId, "master.m3u8", DateTimeOffset.UtcNow.AddSeconds(-1));
        }

        Assert.Equal("Playback.InvalidSignature", await MasterAsync(started.SessionId, expired));
    }

    [Fact]
    public async Task TheRules_AreCheckedInOrder()
    {
        var setup = await SetUpAsync(VideoQuality.Hd, screens: 1, titleRating: AgeRating.Age16, profileRating: AgeRating.Age13);
        using var client = api.CreateClientFor(setup.User);

        Assert.Equal("Playback.AgeRestricted", await StartCodeAsync(client, setup.ProfileId, setup.ContentId));
        Assert.Equal("Playback.NotAvailable", await StartCodeAsync(client, setup.ProfileId, Guid.CreateVersion7()));
        Assert.Equal("Playback.ProfileNotFound", await StartCodeAsync(client, Guid.CreateVersion7(), setup.ContentId));

        var stranger = await SetUpAsync(VideoQuality.Hd, screens: 1);
        Assert.Equal("Playback.ProfileNotFound", await StartCodeAsync(client, stranger.ProfileId, setup.ContentId));

        using var unsubscribed = api.CreateClientFor(Guid.CreateVersion7());
        var profileOfUnsubscribed = Guid.CreateVersion7();
        await SendAsync(new ApplyProfileCommand(profileOfUnsubscribed, UserOf(unsubscribed), AgeRating.Age18, 1));
        Assert.Equal("Playback.SubscriptionRequired", await StartCodeAsync(unsubscribed, profileOfUnsubscribed, setup.ContentId));

        await SendAsync(new ApplyProfileCommand(setup.ProfileId, setup.User, AgeRating.Age18, 2));
        Assert.Null(await StartCodeAsync(client, setup.ProfileId, setup.ContentId));

        await SendAsync(new ApplyProfileCommand(setup.ProfileId, setup.User, null, 3));
        Assert.Equal("Playback.ProfileNotFound", await StartCodeAsync(client, setup.ProfileId, setup.ContentId));

        var invalid = await client.PostAsJsonAsync("/playback-sessions", new { profileId = Guid.Empty, contentId = Guid.Empty }, Json, Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var anonymous = api.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/playback-sessions", new { setup.ProfileId, setup.ContentId }, Json, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Screens_AreLimited_AndFreedByEndingOrSilence()
    {
        var setup = await SetUpAsync(VideoQuality.Hd, screens: 1);
        using var client = api.CreateClientFor(setup.User);
        var first = await StartAsync(client, setup);

        Assert.Equal("Playback.TooManyScreens", await StartCodeAsync(client, setup.ProfileId, setup.ContentId));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/playback-sessions/{first.SessionId}", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/playback-sessions/{first.SessionId}", Cancellation)).StatusCode);
        var second = await StartAsync(client, setup);

        await SilenceAsync(second.SessionId);
        Assert.Equal("Playback.SessionEnded", await CodeAsync(await client.PostAsync($"/playback-sessions/{second.SessionId}/heartbeat", null, Cancellation)));
        Assert.Null(await StartCodeAsync(client, setup.ProfileId, setup.ContentId));

        using var stranger = api.CreateClientFor(Guid.CreateVersion7());
        Assert.Equal("Playback.SessionEnded", await CodeAsync(await stranger.DeleteAsync($"/playback-sessions/{first.SessionId}", Cancellation)));
    }

    [Fact]
    public async Task TwoStartsAtOnce_CannotBothTakeTheLastScreen()
    {
        var setup = await SetUpAsync(VideoQuality.Hd, screens: 1);
        using var client = api.CreateClientFor(setup.User);

        // Whether they overlap or not, one start takes the screen: the rest see it taken or lose the save.
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/playback-sessions", new { setup.ProfileId, setup.ContentId }, Json, Cancellation)));

        Assert.Single(results, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(results.Where(response => response.StatusCode != HttpStatusCode.Created), response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
    }

    [Fact]
    public async Task TwoStartsThatBothSawAFreeScreen_CannotBothBeSaved()
    {
        var setup = await SetUpAsync(VideoQuality.Hd, screens: 1);
        using var client = api.CreateClientFor(setup.User);
        var first = await StartAsync(client, setup);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/playback-sessions/{first.SessionId}", Cancellation)).StatusCode);

        await using var one = api.CreateScope();
        await using var other = api.CreateScope();
        var grant = new Domain.Playback.PlaybackGrant(setup.ProfileId, setup.ContentId, setup.TitleId, setup.VideoId, 1080);
        var oneViewer = await one.ServiceProvider.GetRequiredService<Domain.Playback.IViewerRepository>().GetAsync(setup.User, Cancellation);
        var otherViewer = await other.ServiceProvider.GetRequiredService<Domain.Playback.IViewerRepository>().GetAsync(setup.User, Cancellation);
        Assert.True(oneViewer!.Start(grant, 1, DateTimeOffset.UtcNow).IsSuccess);
        Assert.True(otherViewer!.Start(grant, 1, DateTimeOffset.UtcNow).IsSuccess);

        await one.ServiceProvider.GetRequiredService<Shared.Application.IUnitOfWork>().SaveChangesAsync(Cancellation);
        await Assert.ThrowsAsync<Shared.Application.DataConflictException>(() => other.ServiceProvider.GetRequiredService<Shared.Application.IUnitOfWork>().SaveChangesAsync(Cancellation));
    }

    [Fact]
    public async Task AnEndedOrSilentSession_CannotBeRevivedByAHeartbeat()
    {
        var setup = await SetUpAsync(VideoQuality.Hd, screens: 2);
        using var client = api.CreateClientFor(setup.User);
        var ended = await StartAsync(client, setup);
        var silent = await StartAsync(client, setup);
        await client.DeleteAsync($"/playback-sessions/{ended.SessionId}", Cancellation);
        await SilenceAsync(silent.SessionId);

        await using var scope = api.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<Domain.Playback.IPlaybackSessionRepository>();
        Assert.False(await sessions.TryHeartbeatAsync(ended.SessionId, setup.User, DateTimeOffset.UtcNow, Cancellation));
        Assert.False(await sessions.TryHeartbeatAsync(silent.SessionId, setup.User, DateTimeOffset.UtcNow, Cancellation));
        Assert.False(await sessions.TryHeartbeatAsync(silent.SessionId, Guid.CreateVersion7(), DateTimeOffset.UtcNow, Cancellation));
    }

    [Fact]
    public async Task AHeartbeat_KeepsTheSessionLive_UntilWatchingIsNoLongerAllowed()
    {
        var setup = await SetUpAsync(VideoQuality.Hd, screens: 1);
        using var client = api.CreateClientFor(setup.User);
        var started = await StartAsync(client, setup);
        await using (var scope = api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().PlaybackSessions.Where(session => session.Id == started.SessionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.LastHeartbeatAt, DateTimeOffset.UtcNow.AddSeconds(-90)), Cancellation);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/playback-sessions/{started.SessionId}/heartbeat", null, Cancellation)).StatusCode);
        Assert.True((await SessionAsync(started.SessionId)).LastHeartbeatAt > DateTimeOffset.UtcNow.AddSeconds(-10));

        using var stranger = api.CreateClientFor(Guid.CreateVersion7());
        Assert.Equal("Playback.SessionEnded", await CodeAsync(await stranger.PostAsync($"/playback-sessions/{started.SessionId}/heartbeat", null, Cancellation)));

        await SendAsync(new ApplySubscriptionCommand(setup.SubscriptionId, setup.User, SubscriptionStatus.Suspended, null, null, 2));
        Assert.Equal("Playback.SubscriptionRequired", await CodeAsync(await client.PostAsync($"/playback-sessions/{started.SessionId}/heartbeat", null, Cancellation)));
        Assert.NotNull((await SessionAsync(started.SessionId)).EndedAt);

        using var player = api.CreateAnonymousClient();
        Assert.Equal("Playback.SessionEnded", await CodeAsync(await player.GetAsync(new Uri(new Uri(player.BaseAddress!, "/playback-sessions"), started.Playlist), Cancellation)));
    }

    [Fact]
    public async Task ATitleTakenDown_EndsItsSessionsAtTheNextHeartbeat()
    {
        var setup = await SetUpAsync(VideoQuality.Hd, screens: 1);
        using var client = api.CreateClientFor(setup.User);
        var started = await StartAsync(client, setup);

        await SendAsync(new ApplyPlayableCommand(setup.TitleId, TitleKind.Movie, AgeRating.All, [], 2));

        Assert.Equal("Playback.NotAvailable", await CodeAsync(await client.PostAsync($"/playback-sessions/{started.SessionId}/heartbeat", null, Cancellation)));
        Assert.Equal("Playback.NotAvailable", await StartCodeAsync(client, setup.ProfileId, setup.ContentId));
    }

    [Fact]
    public async Task OldSessions_AreCleanedUp_AfterADay()
    {
        var setup = await SetUpAsync(VideoQuality.Hd, screens: 2);
        using var client = api.CreateClientFor(setup.User);
        var old = await StartAsync(client, setup);
        var recent = await StartAsync(client, setup);
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
        await context.PlaybackSessions.Where(session => session.Id == old.SessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.LastHeartbeatAt, DateTimeOffset.UtcNow.AddDays(-2)), Cancellation);

        await scope.ServiceProvider.GetRequiredService<Infrastructure.Playback.PlaybackSessionCleaner>().CleanAsync(Cancellation);

        Assert.False(await context.PlaybackSessions.AnyAsync(session => session.Id == old.SessionId, Cancellation));
        Assert.True(await context.PlaybackSessions.AnyAsync(session => session.Id == recent.SessionId, Cancellation));
    }

    private sealed record Setup(Guid User, Guid ProfileId, Guid SubscriptionId, Guid TitleId, Guid ContentId, Guid VideoId);

    /// <summary>A user with a subscription and a profile, and a movie that can be watched, its files in storage.</summary>
    private async Task<Setup> SetUpAsync(VideoQuality quality, int screens, AgeRating titleRating = AgeRating.Age13, AgeRating profileRating = AgeRating.Age18)
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var (user, profile, subscription, movie) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        var now = DateTimeOffset.UtcNow;
        var video = Video.StartUpload(ContentKind.Movie, movie, now).Value;
        video.ConfirmUpload(1000, now);
        video.MarkReady(600, [new Rendition(360, 640, 800_000, "360p/index.m3u8"), new Rendition(720, 1280, 2_800_000, "720p/index.m3u8"), new Rendition(1080, 1920, 5_000_000, "1080p/index.m3u8")]);
        await using (var scope = api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
            context.Videos.Add(video);
            await context.SaveChangesAsync(Cancellation);
        }

        var videos = new BlobServiceClient(api.VideosConnectionString).GetBlobContainerClient(VideoBlobStorage.VideosContainer);
        foreach (var height in new[] { 360, 720, 1080 })
        {
            var folder = $"{VideoBlobStorage.FolderOf(video.Id)}/{height}p";
            await videos.GetBlobClient($"{folder}/index.m3u8").UploadAsync(BinaryData.FromString("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:6\n#EXTINF:6.000000,\nseg_0000.ts\n#EXT-X-ENDLIST\n"), Cancellation);
            await videos.GetBlobClient($"{folder}/seg_0000.ts").UploadAsync(BinaryData.FromBytes(Segment), Cancellation);
        }

        await SendAsync(new ApplySubscriptionCommand(subscription, user, SubscriptionStatus.Active, quality, screens, 1));
        await SendAsync(new ApplyProfileCommand(profile, user, profileRating, 1));
        await SendAsync(new ApplyPlayableCommand(movie, TitleKind.Movie, titleRating, [new PlayableItem(movie, video.Id)], 1));
        return new Setup(user, profile, subscription, movie, movie, video.Id);
    }

    private static async Task<PlaybackStarted> StartAsync(HttpClient client, Setup setup)
        => await VideoApiTests.ReadAsync<PlaybackStarted>(await client.PostAsJsonAsync("/playback-sessions", new { setup.ProfileId, setup.ContentId }, Json, Cancellation));

    /// <summary>The error code of a refused start, or <see langword="null"/> when it started.</summary>
    private static async Task<string?> StartCodeAsync(HttpClient client, Guid profileId, Guid contentId)
        => await CodeAsync(await client.PostAsJsonAsync("/playback-sessions", new { profileId, contentId }, Json, Cancellation));

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Cancellation);
        return problem?.Extensions.TryGetValue("code", out var code) == true ? code?.ToString() : $"{(int)response.StatusCode}";
    }

    private static Guid UserOf(HttpClient client)
    {
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        return Guid.Parse(new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token).Subject);
    }

    private async Task SendAsync(ICommand command)
    {
        await using var scope = api.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(command, Cancellation);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
    }

    private async Task SilenceAsync(Guid sessionId)
    {
        await using var scope = api.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().PlaybackSessions.Where(session => session.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.LastHeartbeatAt, DateTimeOffset.UtcNow - Domain.Playback.PlaybackSession.Timeout), Cancellation);
    }

    private async Task<Domain.Playback.PlaybackSession> SessionAsync(Guid sessionId)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStreamingReadModel>().PlaybackSessions.SingleAsync(session => session.Id == sessionId, Cancellation);
    }
}
