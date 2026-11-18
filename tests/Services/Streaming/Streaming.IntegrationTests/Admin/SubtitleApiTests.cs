using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetBoxPlus.Testing;
using Streaming.Application.Videos.GetVideos;
using Streaming.Application.Videos.Subtitles;
using Streaming.Domain.Videos;
using Streaming.Infrastructure.Media;
using Streaming.Infrastructure.Persistence;

namespace Streaming.IntegrationTests.Admin;

/// <summary>Subtitles through the admin API, against Azurite: no conversion by the transcoder is involved.</summary>
[Collection(nameof(StreamingApiCollection))]
public sealed class SubtitleApiTests(StreamingApiFixture api)
{
    private const string Srt = "1\n00:00:01,000 --> 00:00:02,000\nHello.\n";

    private static readonly JsonSerializerOptions Json = VideoApiTests.CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnSrtFile_IsAccepted_AsWebVtt()
    {
        using var admin = Admin();
        var id = await ReadyVideoAsync();

        var ticket = await StartAsync(admin, id, "en-US");
        Assert.Equal(Subtitle.MaxBytes, ticket.MaxBytes);
        await PutAsync(ticket, Srt);
        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(admin, id, ticket)).StatusCode);

        var subtitle = Assert.Single((await DetailsAsync(admin, id)).Subtitles);
        Assert.Equal((ticket.SubtitleId, "en-US", TrackStatus.Ready), (subtitle.Id, subtitle.Language, subtitle.Status));
        var file = StorageConnections.CreateClient(api.VideosConnectionString).GetBlobContainerClient(VideoBlobStorage.VideosContainer)
            .GetBlobClient($"{id:N}/subtitles/{ticket.SubtitleId:N}.vtt");
        Assert.Equal("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nHello.\n", (await file.DownloadContentAsync(Cancellation)).Value.Content.ToString());
        Assert.Equal("text/vtt", (await file.GetPropertiesAsync(cancellationToken: Cancellation)).Value.ContentType);
        Assert.False(await StorageConnections.CreateClient(api.VideosConnectionString).GetBlobContainerClient(VideoBlobStorage.UploadsContainer)
            .GetBlobClient(ticket.SubtitleId.ToString("N")).ExistsAsync(Cancellation));
    }

    [Fact]
    public async Task AFileThatIsNoSubtitles_IsRefused_AndTheSameAddressTakesACorrectedOne()
    {
        using var admin = Admin();
        var id = await ReadyVideoAsync();
        var ticket = await StartAsync(admin, id, "tr-TR");

        Assert.Equal((HttpStatusCode.Conflict, "Video.NotUploaded"), await ProblemAsync(await ConfirmAsync(admin, id, ticket)));
        await PutAsync(ticket, "not subtitles");
        Assert.Equal((HttpStatusCode.BadRequest, "Video.SubtitleInvalid"), await ProblemAsync(await ConfirmAsync(admin, id, ticket)));
        Assert.Equal(TrackStatus.AwaitingUpload, Assert.Single((await DetailsAsync(admin, id)).Subtitles).Status);

        await PutAsync(ticket, Srt);
        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(admin, id, ticket)).StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, "Video.AlreadyConfirmed"), await ProblemAsync(await ConfirmAsync(admin, id, ticket)));
    }

    [Fact]
    public async Task ATooLargeFile_IsRefused()
    {
        using var admin = Admin();
        var id = await ReadyVideoAsync();
        var ticket = await StartAsync(admin, id, "tr-TR");

        await PutAsync(ticket, Srt + new string('x', (int)Subtitle.MaxBytes));

        Assert.Equal((HttpStatusCode.BadRequest, "Video.SubtitleTooLarge"), await ProblemAsync(await ConfirmAsync(admin, id, ticket)));
    }

    [Fact]
    public async Task TheRules_AreAnsweredAsProblems_AndSubtitlesCanBeRemoved()
    {
        using var admin = Admin();
        var id = await ReadyVideoAsync(AudioLayout.Muxed);
        var ticket = await StartAsync(admin, id, "tr-TR");
        await PutAsync(ticket, Srt);
        await ConfirmAsync(admin, id, ticket);

        Assert.Equal((HttpStatusCode.Conflict, "Video.SubtitleExists"), await ProblemAsync(await StartResponseAsync(admin, id, "tr-TR")));
        Assert.Equal((HttpStatusCode.BadRequest, "Video.LanguageUnsupported"), await ProblemAsync(await StartResponseAsync(admin, id, "xx")));
        Assert.Equal((HttpStatusCode.NotFound, "Video.NotFound"), await ProblemAsync(await StartResponseAsync(admin, Guid.CreateVersion7(), "tr-TR")));
        Assert.Equal((HttpStatusCode.NotFound, "Video.SubtitleNotFound"), await ProblemAsync(await admin.PostAsync($"/admin/videos/{id}/subtitles/{Guid.CreateVersion7()}/uploaded", null, Cancellation)));
        Assert.Equal((HttpStatusCode.NotFound, "Video.SubtitleNotFound"), await ProblemAsync(await admin.DeleteAsync($"/admin/videos/{id}/subtitles/en-US", Cancellation)));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/admin/videos/{id}/subtitles/tr-TR", Cancellation)).StatusCode);
        Assert.Empty((await DetailsAsync(admin, id)).Subtitles);

        using var reader = api.CreateClient("streaming:videos:read");
        Assert.Equal(HttpStatusCode.Forbidden, (await StartResponseAsync(reader, id, "tr-TR")).StatusCode);
    }

    private HttpClient Admin() => api.CreateClient("streaming:videos:read", "streaming:videos:write");

    /// <summary>A converted video, written as the transcoder would leave it; the old layout takes subtitles too.</summary>
    private async Task<Guid> ReadyVideoAsync(AudioLayout layout = AudioLayout.Separate)
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var video = Video.StartUpload(ContentKind.Movie, Guid.CreateVersion7(), MediaLanguages.Default, DateTimeOffset.UtcNow).Value;
        video.ConfirmUpload(1000, DateTimeOffset.UtcNow);
        video.MarkReady(60, [new Rendition(360, 640, 800_000, "360p/index.m3u8")]);
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
        context.Videos.Add(video);
        await context.SaveChangesAsync(Cancellation);
        await context.Videos.Where(item => item.Id == video.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.AudioLayout, layout), Cancellation);
        return video.Id;
    }

    private static Task<HttpResponseMessage> StartResponseAsync(HttpClient admin, Guid videoId, string language)
        => admin.PostAsJsonAsync($"/admin/videos/{videoId}/subtitles", new { language }, Json, Cancellation);

    private static async Task<SubtitleUploadTicket> StartAsync(HttpClient admin, Guid videoId, string language)
        => await VideoApiTests.ReadAsync<SubtitleUploadTicket>(await StartResponseAsync(admin, videoId, language));

    private static Task PutAsync(SubtitleUploadTicket ticket, string text)
        => TestVideos.PutAsync(ticket.UploadAddress, System.Text.Encoding.UTF8.GetBytes(text));

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient admin, Guid videoId, SubtitleUploadTicket ticket)
        => admin.PostAsync($"/admin/videos/{videoId}/subtitles/{ticket.SubtitleId}/uploaded", null, Cancellation);

    private static async Task<VideoDetails> DetailsAsync(HttpClient admin, Guid id)
        => await VideoApiTests.ReadAsync<VideoDetails>(await admin.GetAsync($"/admin/videos/{id}", Cancellation));

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Cancellation);
        return (response.StatusCode, problem?.Extensions.TryGetValue("code", out var code) == true ? code?.ToString() : null);
    }
}
