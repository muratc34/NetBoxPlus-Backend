using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetBoxPlus.Testing;
using Streaming.Application.Videos.GetVideos;
using Streaming.Domain.Videos;
using Streaming.Infrastructure.Media;
using Streaming.Infrastructure.Persistence;

namespace Streaming.IntegrationTests.Admin;

/// <summary>The converter on real files: ffmpeg, Azurite and PostgreSQL, as the transcoder process runs it.</summary>
[Collection(nameof(StreamingApiCollection))]
public sealed class TranscodeTests(StreamingApiFixture api)
{
    private static readonly JsonSerializerOptions Json = VideoApiTests.CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private BlobContainerClient Videos => new BlobServiceClient(api.VideosConnectionString).GetBlobContainerClient(VideoBlobStorage.VideosContainer);

    private BlobContainerClient Uploads => new BlobServiceClient(api.VideosConnectionString).GetBlobContainerClient(VideoBlobStorage.UploadsContainer);

    [Fact]
    public async Task AVideo_BecomesHls_InTheRenditionsItsSourceAllows()
    {
        var file = await TestVideos.CreateAsync(1280, 720, 3, audio: true);
        var id = await QueueAsync(file);

        Assert.True(await ProcessAsync());

        var details = await DetailsAsync(id);
        Assert.Equal(VideoStatus.Ready, details.Status);
        Assert.InRange(details.DurationSeconds!.Value, 2.5, 3.5);
        Assert.Equal([(360, 640), (720, 1280)], details.Renditions.Select(rendition => (rendition.Height, rendition.Width)));

        var folder = id.ToString("N");
        var master = (await Videos.GetBlobClient($"{folder}/master.m3u8").DownloadContentAsync(Cancellation)).Value.Content.ToString();
        Assert.StartsWith("#EXTM3U", master, StringComparison.Ordinal);
        Assert.Contains("RESOLUTION=1280x720", master, StringComparison.Ordinal);
        Assert.Contains("360p/index.m3u8", master, StringComparison.Ordinal);
        var playlist = (await Videos.GetBlobClient($"{folder}/720p/index.m3u8").DownloadContentAsync(Cancellation)).Value.Content.ToString();
        Assert.Contains("#EXT-X-ENDLIST", playlist, StringComparison.Ordinal);
        Assert.True(await Videos.GetBlobClient($"{folder}/720p/seg_0000.ts").ExistsAsync(Cancellation));

        Assert.False(await Uploads.GetBlobClient(folder).ExistsAsync(Cancellation));
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
        Assert.False(await context.Set<TranscodeJob>().AnyAsync(job => job.VideoId == id, Cancellation));
        Assert.Contains(await PayloadsAsync(context, "VideoReady"), payload => payload.Contains(id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASmallSilentVideo_KeepsItsOwnHeight()
    {
        var id = await QueueAsync(await TestVideos.CreateAsync(320, 180, 2, audio: false));

        Assert.True(await ProcessAsync());

        var details = await DetailsAsync(id);
        Assert.Equal(VideoStatus.Ready, details.Status);
        Assert.Equal(180, Assert.Single(details.Renditions).Height);
    }

    [Fact]
    public async Task AFileThatIsNoVideo_Fails_AndIsAnnounced()
    {
        FfmpegAvailability.SkipUnlessAvailable();
        var id = await QueueAsync("this is not a video at all"u8.ToArray());

        Assert.True(await ProcessAsync());

        var details = await DetailsAsync(id);
        Assert.Equal((VideoStatus.Failed, "The file is not a readable video."), (details.Status, details.FailureReason));
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
        Assert.Contains(await PayloadsAsync(context, "VideoFailed"), payload => payload.Contains(id.ToString(), StringComparison.Ordinal));
        Assert.False(await context.Set<TranscodeJob>().AnyAsync(job => job.VideoId == id, Cancellation));
    }

    [Fact]
    public async Task TwoWorkers_NeverTakeTheSameVideo()
    {
        var id = await QueueAsync(await TestVideos.CreateAsync(320, 180, 1, audio: false));

        var results = await Task.WhenAll(ProcessAsync(), ProcessAsync());

        Assert.Equal(1, results.Count(worked => worked));
        Assert.Equal(VideoStatus.Ready, (await DetailsAsync(id)).Status);
    }

    [Fact]
    public async Task AVideoLeftByADeadWorker_IsTakenAgain()
    {
        var id = await QueueAsync(await TestVideos.CreateAsync(320, 180, 1, audio: false));
        await using (var scope = api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().Set<TranscodeJob>().Where(job => job.VideoId == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.LeasedUntil, DateTimeOffset.UtcNow.AddHours(1)), Cancellation);
        }

        Assert.False(await ProcessAsync());

        await using (var scope = api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().Set<TranscodeJob>().Where(job => job.VideoId == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.LeasedUntil, DateTimeOffset.UtcNow.AddMinutes(-1)), Cancellation);
        }

        Assert.True(await ProcessAsync());
        Assert.Equal(VideoStatus.Ready, (await DetailsAsync(id)).Status);
    }

    [Fact]
    public async Task TroubleAroundTheFile_IsTriedAgainLater_ThenGivenUp()
    {
        FfmpegAvailability.SkipUnlessAvailable();
        var id = await QueueAsync([1, 2, 3]);
        await Uploads.GetBlobClient(id.ToString("N")).DeleteAsync(cancellationToken: Cancellation);

        Assert.True(await ProcessAsync());

        await using (var scope = api.CreateScope())
        {
            var job = await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().Set<TranscodeJob>().AsNoTracking().SingleAsync(item => item.VideoId == id, Cancellation);
            Assert.Equal(1, job.Attempts);
            Assert.Null(job.LeasedUntil);
            Assert.True(job.AvailableAt > DateTimeOffset.UtcNow);
            Assert.NotNull(job.LastError);
        }

        Assert.Equal(VideoStatus.Queued, (await DetailsAsync(id)).Status);

        await using (var scope = api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().Set<TranscodeJob>().Where(job => job.VideoId == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.AvailableAt, DateTimeOffset.UtcNow.AddMinutes(-1)).SetProperty(job => job.Attempts, TranscodeProcessor.MaxAttempts - 1), Cancellation);
        }

        Assert.True(await ProcessAsync());

        var details = await DetailsAsync(id);
        Assert.Equal((VideoStatus.Failed, $"Converting failed {TranscodeProcessor.MaxAttempts} times."), (details.Status, details.FailureReason));
    }

    /// <summary>Queues the file as the only job, so that "the next" video is this test's; other tests leave theirs behind.</summary>
    private async Task<Guid> QueueAsync(byte[] file)
    {
        await DrainAsync();
        using var admin = api.CreateClient("streaming:videos:read", "streaming:videos:write");
        var ticket = await TestVideos.StartAsync(admin, "Movie", Guid.CreateVersion7(), Json);
        await TestVideos.PutAsync(ticket.UploadAddress, file);
        var confirmed = await admin.PostAsync($"/admin/videos/{ticket.VideoId}/uploaded", null, Cancellation);
        Assert.True(confirmed.IsSuccessStatusCode, await confirmed.Content.ReadAsStringAsync(Cancellation));
        return ticket.VideoId;
    }

    private async Task<bool> ProcessAsync()
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TranscodeProcessor>().ProcessNextAsync(Cancellation);
    }

    /// <summary>Removes every queued job.</summary>
    private async Task DrainAsync()
    {
        await using var scope = api.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().Set<TranscodeJob>().ExecuteDeleteAsync(Cancellation);
    }

    private async Task<VideoDetails> DetailsAsync(Guid id)
    {
        using var admin = api.CreateClient("streaming:videos:read");
        return await VideoApiTests.ReadAsync<VideoDetails>(await admin.GetAsync($"/admin/videos/{id}", Cancellation));
    }

    private static Task<List<string>> PayloadsAsync(StreamingDbContext context, string type)
        => context.OutboxMessages.Where(message => message.Type == "Streaming.Contracts." + type).Select(message => message.Payload).ToListAsync(Cancellation);
}
