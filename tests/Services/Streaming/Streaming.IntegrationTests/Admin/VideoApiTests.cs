using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Paging;
using Streaming.Application.Videos.GetVideos;
using Streaming.Domain.Videos;
using Streaming.Infrastructure.Media;
using Streaming.Infrastructure.Persistence;

namespace Streaming.IntegrationTests.Admin;

[Collection(nameof(StreamingApiCollection))]
public sealed class VideoApiTests(StreamingApiFixture api)
{
    private const string Read = "streaming:videos:read";
    private const string Write = "streaming:videos:write";
    private static readonly JsonSerializerOptions Json = CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("POST", "/admin/videos", Write)]
    [InlineData("POST", "/admin/videos/01a1bbcf-1a47-7189-a777-c599de32e2de/uploaded", Write)]
    [InlineData("GET", "/admin/videos", Read)]
    [InlineData("GET", "/admin/videos/01a1bbcf-1a47-7189-a777-c599de32e2de", Read)]
    public async Task Endpoints_NeedATokenAndTheirPermission(string method, string path, string permission)
    {
        using var anonymous = api.CreateAnonymousClient();
        using var unrelated = api.CreateClient(new[] { Read, Write }.Where(item => item != permission).ToArray());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unrelated.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Cancellation)).StatusCode);
    }

    [Fact]
    public async Task AnUploadedVideo_IsQueued_Announced_AndAudited()
    {
        using var admin = api.CreateClient(Read, Write);
        var content = Guid.CreateVersion7();

        var response = await admin.PostAsJsonAsync("/admin/videos", new { contentKind = "Episode", contentId = content }, Json, Cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = (await response.Content.ReadFromJsonAsync<Streaming.Application.Videos.StartVideoUpload.VideoUploadTicket>(Json, Cancellation))!;
        Assert.Equal($"/admin/videos/{ticket.VideoId}", response.Headers.Location?.OriginalString);
        Assert.Equal(Video.MaxBytes, ticket.MaxBytes);

        await TestVideos.PutAsync(ticket.UploadAddress, [1, 2, 3, 4]);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/admin/videos/{ticket.VideoId}/uploaded", null, Cancellation)).StatusCode);

        var details = await ReadAsync<VideoDetails>(await admin.GetAsync($"/admin/videos/{ticket.VideoId}", Cancellation));
        Assert.Equal((VideoStatus.Queued, ContentKind.Episode, content, 4L), (details.Status, details.ContentKind, details.ContentId, details.SizeBytes));

        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
        Assert.True(await context.Set<TranscodeJob>().AnyAsync(job => job.VideoId == ticket.VideoId, Cancellation));
        Assert.Contains(await context.OutboxMessages.Where(message => message.Type == "Streaming.Contracts.VideoQueued").Select(message => message.Payload).ToListAsync(Cancellation), payload => payload.Contains(ticket.VideoId.ToString(), StringComparison.Ordinal));
        Assert.Equal(["StartVideoUpload", "ConfirmVideoUpload"], await context.AuditLogEntries.Where(entry => entry.TargetId == content || entry.TargetId == ticket.VideoId).OrderBy(entry => entry.OccurredAt).Select(entry => entry.Action).ToListAsync(Cancellation));
    }

    [Fact]
    public async Task ConfirmingWithoutAFile_Twice_OrForNothing_IsRefused()
    {
        using var admin = api.CreateClient(Read, Write);
        var ticket = await TestVideos.StartAsync(admin, "Movie", Guid.CreateVersion7(), Json);

        await AssertProblemAsync(await admin.PostAsync($"/admin/videos/{ticket.VideoId}/uploaded", null, Cancellation), HttpStatusCode.Conflict, "Video.NotUploaded");
        await TestVideos.PutAsync(ticket.UploadAddress, [1]);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/admin/videos/{ticket.VideoId}/uploaded", null, Cancellation)).StatusCode);
        await AssertProblemAsync(await admin.PostAsync($"/admin/videos/{ticket.VideoId}/uploaded", null, Cancellation), HttpStatusCode.Conflict, "Video.AlreadyConfirmed");
        await AssertProblemAsync(await admin.PostAsync($"/admin/videos/{Guid.NewGuid()}/uploaded", null, Cancellation), HttpStatusCode.NotFound, "Video.NotFound");
        await AssertProblemAsync(await admin.PostAsJsonAsync("/admin/videos", new { contentKind = "Movie", contentId = Guid.Empty }, Json, Cancellation), HttpStatusCode.BadRequest, "Video.ContentRequired");
    }

    [Fact]
    public async Task Videos_AreListedByContentAndStatus()
    {
        using var admin = api.CreateClient(Read, Write);
        var content = Guid.CreateVersion7();
        var waiting = await TestVideos.StartAsync(admin, "Movie", content, Json);
        var queued = await TestVideos.StartAsync(admin, "Movie", content, Json);
        await TestVideos.PutAsync(queued.UploadAddress, [1]);
        await admin.PostAsync($"/admin/videos/{queued.VideoId}/uploaded", null, Cancellation);

        var all = await ReadAsync<Page<VideoSummary>>(await admin.GetAsync($"/admin/videos?contentId={content}", Cancellation));
        var onlyQueued = await ReadAsync<Page<VideoSummary>>(await admin.GetAsync($"/admin/videos?contentId={content}&status=Queued", Cancellation));

        Assert.Equal([queued.VideoId, waiting.VideoId], all.Items.Select(item => item.Id));
        Assert.Equal(queued.VideoId, Assert.Single(onlyQueued.Items).Id);
    }

    [Fact]
    public async Task UnfinishedUploads_ExpireAndTheirFilesGo()
    {
        using var admin = api.CreateClient(Read, Write);
        var ticket = await TestVideos.StartAsync(admin, "Movie", Guid.CreateVersion7(), Json);
        await TestVideos.PutAsync(ticket.UploadAddress, [1, 2]);

        await using (var scope = api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
            await context.Videos.Where(video => video.Id == ticket.VideoId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(video => video.UploadExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Cancellation);
            Assert.True(await scope.ServiceProvider.GetRequiredService<ExpiredUploadCleaner>().CleanAsync(Cancellation) >= 1);
        }

        Assert.Equal(VideoStatus.Expired, (await ReadAsync<VideoDetails>(await admin.GetAsync($"/admin/videos/{ticket.VideoId}", Cancellation))).Status);
        Assert.False(await new BlobServiceClient(api.VideosConnectionString).GetBlobContainerClient(VideoBlobStorage.UploadsContainer).GetBlobClient(ticket.VideoId.ToString("N")).ExistsAsync(Cancellation));
    }

    internal static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    internal static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancellation)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Cancellation))!;
    }

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.True(response.StatusCode == status, $"{(int)response.StatusCode}: {body}");
        Assert.Contains($"\"code\":\"{code}\"", body, StringComparison.Ordinal);
    }
}
