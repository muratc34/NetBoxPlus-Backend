using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using NetBoxPlus.Testing;
using Shared.Application.Cqrs;
using Streaming.Application;
using Streaming.Application.Abstractions;
using Streaming.Application.Access;
using Streaming.Application.Trailers;
using Streaming.Application.Videos.GetVideos;
using Streaming.Domain.Access;
using Streaming.Domain.Videos;
using Streaming.Infrastructure.Media;
using Streaming.Infrastructure.Persistence;
using Streaming.IntegrationTests.Admin;

namespace Streaming.IntegrationTests.Trailers;

/// <summary>Trailers for anyone: the link, the signed playlists and the files, and what keeps a trailer hidden.</summary>
[Collection(nameof(StreamingApiCollection))]
public sealed class TrailerApiTests(StreamingApiFixture api)
{
    private static readonly JsonSerializerOptions Json = VideoApiTests.CreateJson();
    private static readonly byte[] Segment = Encoding.ASCII.GetBytes("not really a transport stream");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anyone_GetsTheTrailerOfAWatchableTitle_UpTo720p_WithItsSound()
    {
        var (title, trailer) = await WatchableTitleWithTrailerAsync(TitleKind.Series);
        using var visitor = api.CreateAnonymousClient();

        var response = await visitor.GetAsync($"/titles/{title}/trailer", Cancellation);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var link = await VideoApiTests.ReadAsync<TrailerLink>(response);
        Assert.Equal(trailer, link.VideoId);
        Assert.Equal(90, link.DurationSeconds);
        Assert.StartsWith($"trailers/{trailer}/master.m3u8?exp=", link.Playlist, StringComparison.Ordinal);
        Assert.InRange(link.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));

        var masterAddress = new Uri(visitor.BaseAddress!, link.Playlist);
        var master = await visitor.GetAsync(masterAddress, Cancellation);
        Assert.Equal(("application/vnd.apple.mpegurl", "no-store"), (master.Content.Headers.ContentType?.MediaType, master.Headers.CacheControl?.ToString()));
        var masterText = await master.Content.ReadAsStringAsync(Cancellation);
        Assert.Contains("RESOLUTION=640x360", masterText, StringComparison.Ordinal);
        Assert.Contains("RESOLUTION=1280x720", masterText, StringComparison.Ordinal);
        Assert.DoesNotContain("1080", masterText, StringComparison.Ordinal);
        var sound = Assert.Single(masterText.Split('\n'), line => line.StartsWith("#EXT-X-MEDIA:TYPE=AUDIO", StringComparison.Ordinal));
        Assert.Contains("LANGUAGE=\"tr-TR\"", sound, StringComparison.Ordinal);
        Assert.Contains("DEFAULT=YES", sound, StringComparison.Ordinal);
        Assert.DoesNotContain("SUBTITLES", masterText, StringComparison.Ordinal);

        using var storage = StorageConnections.CreateHttpClient();
        foreach (var prefix in new[] { "720p/index.m3u8?exp=", "audio/original/index.m3u8?exp=" })
        {
            var line = prefix.StartsWith("audio", StringComparison.Ordinal)
                ? sound[(sound.IndexOf("URI=\"", StringComparison.Ordinal) + 5)..].TrimEnd('"')
                : masterText.Split('\n').First(item => item.StartsWith(prefix, StringComparison.Ordinal));
            var rendition = await visitor.GetStringAsync(new Uri(masterAddress, line), Cancellation);
            var segment = rendition.Split('\n').Single(item => item.Length > 0 && !item.StartsWith('#'));
            Assert.Contains($"/videos/{trailer:N}/", segment, StringComparison.Ordinal);
            Assert.Contains("sp=r", segment, StringComparison.Ordinal);
            Assert.Equal(Segment, await storage.GetByteArrayAsync(segment, Cancellation));
        }

        // The 1080p playlist exists in storage, but a trailer never serves it.
        var tallest = $"trailers/{trailer}/1080p/index.m3u8?{link.Playlist[(link.Playlist.IndexOf('?', StringComparison.Ordinal) + 1)..]}";
        Assert.False((await visitor.GetAsync(tallest, Cancellation)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task NoTrailer_ForATitleNobodyCanWatch_OrOneWhoseTrailerIsNotReady()
    {
        using var visitor = api.CreateAnonymousClient();

        // Ready trailer, but the title is not watchable (unpublished: not in the copy).
        var hidden = Guid.CreateVersion7();
        await AddTrailerAsync(ContentKind.Movie, hidden);
        Assert.Equal("Trailer.NotFound", await CodeAsync(await visitor.GetAsync($"/titles/{hidden}/trailer", Cancellation)));

        // Watchable, but its trailer is still being converted.
        var waiting = Guid.CreateVersion7();
        await PlayableAsync(waiting, TitleKind.Movie);
        await AddTrailerAsync(ContentKind.Movie, waiting, ready: false);
        Assert.Equal("Trailer.NotFound", await CodeAsync(await visitor.GetAsync($"/titles/{waiting}/trailer", Cancellation)));

        // Watchable, with its own video only.
        var plain = Guid.CreateVersion7();
        await PlayableAsync(plain, TitleKind.Movie);
        Assert.Equal("Trailer.NotFound", await CodeAsync(await visitor.GetAsync($"/titles/{plain}/trailer", Cancellation)));

        // A series' trailer does not show for a movie of the same id, nor the other way round.
        var kindMismatch = Guid.CreateVersion7();
        await PlayableAsync(kindMismatch, TitleKind.Movie);
        await AddTrailerAsync(ContentKind.Series, kindMismatch);
        Assert.Equal("Trailer.NotFound", await CodeAsync(await visitor.GetAsync($"/titles/{kindMismatch}/trailer", Cancellation)));
    }

    [Fact]
    public async Task TheNewestReadyTrailer_IsTheOnePlayed_AndAnOlderOnesLinksStop()
    {
        var (title, older) = await WatchableTitleWithTrailerAsync(TitleKind.Movie);
        using var visitor = api.CreateAnonymousClient();
        var oldLink = await VideoApiTests.ReadAsync<TrailerLink>(await visitor.GetAsync($"/titles/{title}/trailer", Cancellation));

        var newer = await AddTrailerAsync(ContentKind.Movie, title);

        Assert.Equal(newer, (await VideoApiTests.ReadAsync<TrailerLink>(await visitor.GetAsync($"/titles/{title}/trailer", Cancellation))).VideoId);
        Assert.Equal(older, oldLink.VideoId);
        Assert.Equal("Trailer.NotFound", await CodeAsync(await visitor.GetAsync(oldLink.Playlist, Cancellation)));
    }

    [Fact]
    public async Task ATitleTakenOff_StopsItsTrailer_EvenThroughLinksAlreadyGiven()
    {
        var (title, _) = await WatchableTitleWithTrailerAsync(TitleKind.Series);
        using var visitor = api.CreateAnonymousClient();
        var link = await VideoApiTests.ReadAsync<TrailerLink>(await visitor.GetAsync($"/titles/{title}/trailer", Cancellation));

        await SendAsync(new ApplyPlayableCommand(title, TitleKind.Series, AgeRating.Age13, [], 2));

        Assert.Equal("Trailer.NotFound", await CodeAsync(await visitor.GetAsync(link.Playlist, Cancellation)));
        Assert.Equal("Trailer.NotFound", await CodeAsync(await visitor.GetAsync($"/titles/{title}/trailer", Cancellation)));
    }

    [Fact]
    public async Task TrailerAddresses_NeedATrailersOwnValidSignature()
    {
        var (title, trailer) = await WatchableTitleWithTrailerAsync(TitleKind.Movie);
        using var visitor = api.CreateAnonymousClient();
        var link = await VideoApiTests.ReadAsync<TrailerLink>(await visitor.GetAsync($"/titles/{title}/trailer", Cancellation));
        var query = link.Playlist[(link.Playlist.IndexOf('?', StringComparison.Ordinal) + 1)..];

        string sessionSigned;
        string expired;
        await using (var scope = api.CreateScope())
        {
            var signer = scope.ServiceProvider.GetRequiredService<IPlaybackSigner>();
            sessionSigned = signer.Sign(trailer, "master.m3u8", DateTimeOffset.UtcNow.AddHours(1));
            expired = signer.SignTrailer(trailer, "master.m3u8", DateTimeOffset.UtcNow.AddSeconds(-1));
        }

        async Task<string?> MasterAsync(string? queryString)
            => await CodeAsync(await visitor.GetAsync($"/trailers/{trailer}/master.m3u8{(queryString is null ? string.Empty : "?" + queryString)}", Cancellation));

        Assert.Null(await MasterAsync(query));
        Assert.Equal("Playback.InvalidSignature", await MasterAsync(null));
        Assert.Equal("Playback.InvalidSignature", await MasterAsync(query.Replace("sig=", "sig=x", StringComparison.Ordinal)));
        Assert.Equal("Playback.InvalidSignature", await MasterAsync(expired));
        Assert.Equal("Playback.InvalidSignature", await CodeAsync(await visitor.GetAsync($"/trailers/{trailer}/720p/index.m3u8?{query}", Cancellation)));

        // A session's signature does not open a trailer, nor a trailer's a session's playlist.
        Assert.Equal("Playback.InvalidSignature", await MasterAsync(sessionSigned));
        Assert.Equal("Playback.InvalidSignature", await CodeAsync(await visitor.GetAsync($"/playback/{trailer}/master.m3u8?{query}", Cancellation)));
    }

    [Fact]
    public async Task TrailerRequests_AreCounted()
    {
        var (title, _) = await WatchableTitleWithTrailerAsync(TitleKind.Movie);
        using var visitor = api.CreateAnonymousClient();
        using var collector = Collector();

        await visitor.GetAsync($"/titles/{title}/trailer", Cancellation);
        await visitor.GetAsync($"/titles/{Guid.CreateVersion7()}/trailer", Cancellation);
        await visitor.GetAsync($"/titles/{Guid.CreateVersion7()}/trailer", Cancellation);

        // The tests of this collection run one at a time against this host, so these are this test's requests only.
        var counts = collector.GetMeasurementSnapshot().GroupBy(measurement => (string)measurement.Tags["outcome"]!).ToDictionary(group => group.Key, group => group.Sum(item => item.Value));
        Assert.Equal((1L, 2L), (counts.GetValueOrDefault("found"), counts.GetValueOrDefault("not_found")));
    }

    [Fact]
    public async Task AnAdmin_UploadsATrailer_ThatIsNotAnnounced_AndTakesNoTracks()
    {
        using var admin = api.CreateClient("streaming:videos:read", "streaming:videos:write");
        var series = Guid.CreateVersion7();

        var ticket = await StartTrailerAsync(admin, "Series", series);
        await TestVideos.PutAsync(ticket.UploadAddress, [1, 2, 3]);
        Assert.True((await admin.PostAsync($"/admin/videos/{ticket.VideoId}/uploaded", null, Cancellation)).IsSuccessStatusCode);

        var details = await VideoApiTests.ReadAsync<VideoDetails>(await admin.GetAsync($"/admin/videos/{ticket.VideoId}", Cancellation));
        Assert.Equal((VideoPurpose.Trailer, ContentKind.Series, VideoStatus.Queued), (details.Purpose, details.ContentKind, details.Status));
        var listed = await VideoApiTests.ReadAsync<Shared.Application.Paging.Page<VideoSummary>>(await admin.GetAsync($"/admin/videos?contentId={series}&purpose=Trailer", Cancellation));
        Assert.Equal(ticket.VideoId, Assert.Single(listed.Items).Id);
        Assert.Empty((await VideoApiTests.ReadAsync<Shared.Application.Paging.Page<VideoSummary>>(await admin.GetAsync($"/admin/videos?contentId={series}&purpose=Feature", Cancellation))).Items);

        await using (var scope = api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
            Assert.DoesNotContain(await VideoEventPayloadsAsync(context), payload => payload.Contains(ticket.VideoId.ToString(), StringComparison.Ordinal));
            Assert.True(await context.Set<TranscodeJob>().AnyAsync(job => job.VideoId == ticket.VideoId, Cancellation));
        }

        var episodeTrailer = await admin.PostAsJsonAsync("/admin/videos", new { contentKind = "Episode", contentId = Guid.CreateVersion7(), purpose = "Trailer" }, Json, Cancellation);
        var seriesFeature = await admin.PostAsJsonAsync("/admin/videos", new { contentKind = "Series", contentId = Guid.CreateVersion7() }, Json, Cancellation);
        Assert.Equal("Video.ContentKindInvalid", await CodeAsync(episodeTrailer));
        Assert.Equal("Video.ContentKindInvalid", await CodeAsync(seriesFeature));

        var (_, ready) = await WatchableTitleWithTrailerAsync(TitleKind.Movie);
        var dub = await admin.PostAsJsonAsync($"/admin/videos/{ready}/audio-tracks", new { language = "en-US" }, Json, Cancellation);
        var subtitles = await admin.PostAsJsonAsync($"/admin/videos/{ready}/subtitles", new { language = "en-US" }, Json, Cancellation);
        Assert.Equal((HttpStatusCode.Conflict, "Video.TrailerHasNoTracks"), (dub.StatusCode, await CodeAsync(dub)));
        Assert.Equal("Video.TrailerHasNoTracks", await CodeAsync(subtitles));
    }

    [Fact]
    public async Task ATrailer_IsConvertedUpTo720p_AndNeverAnnounced()
    {
        await DrainAsync();
        using var admin = api.CreateClient("streaming:videos:read", "streaming:videos:write");
        var ticket = await StartTrailerAsync(admin, "Movie", Guid.CreateVersion7());
        await TestVideos.PutAsync(ticket.UploadAddress, await TestVideos.CreateAsync(1920, 1080, 2, audio: true));
        Assert.True((await admin.PostAsync($"/admin/videos/{ticket.VideoId}/uploaded", null, Cancellation)).IsSuccessStatusCode);

        await using (var scope = api.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<TranscodeProcessor>().ProcessNextAsync(Cancellation));
        }

        var details = await VideoApiTests.ReadAsync<VideoDetails>(await admin.GetAsync($"/admin/videos/{ticket.VideoId}", Cancellation));
        Assert.Equal(VideoStatus.Ready, details.Status);
        Assert.Equal([360, 720], details.Renditions.Select(rendition => rendition.Height));
        Assert.True(Assert.Single(details.AudioTracks).IsOriginal);
        await using (var scope = api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
            Assert.DoesNotContain(await VideoEventPayloadsAsync(context), payload => payload.Contains(ticket.VideoId.ToString(), StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task TheTranscoder_RefusesAFileLongerThanItsLimit_BeforeConverting()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var source = Path.Combine(Path.GetTempPath(), $"netboxplus-trailer-{Guid.NewGuid():N}.mp4");
        var output = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"netboxplus-trailer-{Guid.NewGuid():N}")).FullName;
        try
        {
            await File.WriteAllBytesAsync(source, await TestVideos.CreateAsync(320, 180, 3, audio: false), Cancellation);
            await using var scope = api.CreateScope();
            var transcoder = scope.ServiceProvider.GetRequiredService<FfmpegTranscoder>();

            var outcome = await transcoder.TranscodeAsync(source, output, new TranscodeLimits(null, TimeSpan.FromSeconds(1)), Cancellation);

            var rejected = Assert.IsType<TranscodeOutcome.Rejected>(outcome);
            Assert.Contains("at most 1 s", rejected.Reason, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(output));
        }
        finally
        {
            File.Delete(source);
            Directory.Delete(output, recursive: true);
        }
    }

    private async Task<(Guid Title, Guid Trailer)> WatchableTitleWithTrailerAsync(TitleKind kind)
    {
        var title = Guid.CreateVersion7();
        await PlayableAsync(title, kind);
        var trailer = await AddTrailerAsync(kind == TitleKind.Movie ? ContentKind.Movie : ContentKind.Series, title);
        return (title, trailer);
    }

    /// <summary>A trailer stored as the transcoder leaves one: 360p, 720p and (more than a trailer serves) 1080p, and its sound.</summary>
    private async Task<Guid> AddTrailerAsync(ContentKind kind, Guid title, bool ready = true)
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var now = DateTimeOffset.UtcNow;
        var video = Video.StartUpload(kind, title, MediaLanguages.Default, now, VideoPurpose.Trailer).Value;
        video.ConfirmUpload(1000, now);
        if (ready)
        {
            video.MarkReady(90, [new Rendition(360, 640, 800_000, "360p/index.m3u8"), new Rendition(720, 1280, 2_800_000, "720p/index.m3u8"), new Rendition(1080, 1920, 5_000_000, "1080p/index.m3u8")], 128_000);
        }

        await using (var scope = api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
            context.Videos.Add(video);
            await context.SaveChangesAsync(Cancellation);
        }

        var videos = StorageConnections.CreateClient(api.VideosConnectionString).GetBlobContainerClient(VideoBlobStorage.VideosContainer);
        foreach (var name in new[] { "360p", "720p", "1080p", "audio/original" })
        {
            var folder = $"{VideoBlobStorage.FolderOf(video.Id)}/{name}";
            await videos.GetBlobClient($"{folder}/index.m3u8").UploadAsync(BinaryData.FromString("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:6\n#EXTINF:6.000000,\nseg_0000.ts\n#EXT-X-ENDLIST\n"), Cancellation);
            await videos.GetBlobClient($"{folder}/seg_0000.ts").UploadAsync(BinaryData.FromBytes(Segment), Cancellation);
        }

        return video.Id;
    }

    /// <summary>The title as the catalog announces a watchable one: with an item and its video.</summary>
    private Task PlayableAsync(Guid title, TitleKind kind)
        => SendAsync(new ApplyPlayableCommand(title, kind, AgeRating.Age13, [new PlayableItem(title, Guid.CreateVersion7())], 1));

    private static async Task<Application.Videos.StartVideoUpload.VideoUploadTicket> StartTrailerAsync(HttpClient admin, string kind, Guid contentId)
        => await VideoApiTests.ReadAsync<Application.Videos.StartVideoUpload.VideoUploadTicket>(
            await admin.PostAsJsonAsync("/admin/videos", new { contentKind = kind, contentId, purpose = "Trailer" }, Json, Cancellation));

    private MetricCollector<long> Collector()
    {
        using var scope = api.CreateScope();
        return new MetricCollector<long>(scope.ServiceProvider.GetRequiredService<IMeterFactory>(), StreamingMetrics.MeterName, "netboxplus.streaming.trailer.requests");
    }

    /// <summary>Every video event in the outbox; filtered here, since the payload is jsonb.</summary>
    private static Task<List<string>> VideoEventPayloadsAsync(StreamingDbContext context)
        => context.OutboxMessages.Where(message => message.Type.StartsWith("Streaming.Contracts.Video")).Select(message => message.Payload).ToListAsync(Cancellation);

    private async Task DrainAsync()
    {
        await using var scope = api.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().Set<TranscodeJob>().ExecuteDeleteAsync(Cancellation);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Cancellation);
        return problem?.Extensions.TryGetValue("code", out var code) == true ? code?.ToString() : $"{(int)response.StatusCode}";
    }

    private async Task SendAsync(ICommand command)
    {
        await using var scope = api.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(command, Cancellation);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
    }
}
