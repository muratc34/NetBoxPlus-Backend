using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetBoxPlus.Testing;
using Streaming.Application.Videos.AudioTracks;
using Streaming.Application.Videos.GetVideos;
using Streaming.Domain.Videos;
using Streaming.Infrastructure.Media;
using Streaming.Infrastructure.Persistence;

namespace Streaming.IntegrationTests.Admin;

/// <summary>A video's sound as tracks of their own, converted by real ffmpeg into Azurite.</summary>
[Collection(nameof(StreamingApiCollection))]
public sealed class AudioTrackTests(StreamingApiFixture api)
{
    private static readonly JsonSerializerOptions Json = VideoApiTests.CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private BlobContainerClient Videos => StorageConnections.CreateClient(api.VideosConnectionString).GetBlobContainerClient(VideoBlobStorage.VideosContainer);

    [Fact]
    public async Task AVideosOwnSound_BecomesTheOriginalTrack_AndTheRenditionsAreSilent()
    {
        using var admin = Admin();
        var id = await ConvertedVideoAsync(admin, "en-GB");

        var details = await DetailsAsync(admin, id);
        var original = Assert.Single(details.AudioTracks);
        Assert.Equal((AudioLayout.Separate, "en-GB", true, TrackStatus.Ready), (details.AudioLayout, original.Language, original.IsOriginal, original.Status));
        Assert.All(details.Renditions, rendition => Assert.Equal(rendition.Height == 720 ? 2_800_000 : 800_000, rendition.Bandwidth));

        var folder = id.ToString("N");
        Assert.True(await Videos.GetBlobClient($"{folder}/audio/original/seg_0000.ts").ExistsAsync(Cancellation));
        Assert.Contains("#EXT-X-ENDLIST", await TextAsync($"{folder}/audio/original/index.m3u8"), StringComparison.Ordinal);
        Assert.False(await HasSoundAsync($"{folder}/720p/seg_0000.ts"));
        Assert.True(await HasSoundAsync($"{folder}/audio/original/seg_0000.ts"));
        Assert.Contains("AUDIO=\"audio\"", await TextAsync($"{folder}/master.m3u8"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADub_IsUploaded_Converted_AndListed()
    {
        using var admin = Admin();
        var id = await ConvertedVideoAsync(admin);

        var track = await UploadDubAsync(admin, id, "en-US", await TestVideos.CreateAudioAsync(3));
        Assert.True(await ProcessAsync());

        var details = await DetailsAsync(admin, id);
        Assert.Equal([("tr-TR", true, TrackStatus.Ready), ("en-US", false, TrackStatus.Ready)], details.AudioTracks.Select(item => (item.Language, item.IsOriginal, item.Status)));
        var folder = $"{id:N}/audio/{track:N}";
        Assert.Contains("#EXT-X-ENDLIST", await TextAsync($"{folder}/index.m3u8"), StringComparison.Ordinal);
        Assert.True(await HasSoundAsync($"{folder}/seg_0000.ts"));

        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
        Assert.False(await context.Set<TranscodeJob>().AnyAsync(job => job.Id == track, Cancellation));
        var announced = (await context.OutboxMessages
            .Where(message => message.Type == "Streaming.Contracts.VideoTracksChanged")
            .Select(message => message.Payload)
            .ToListAsync(Cancellation))
            .Where(payload => payload.Contains(id.ToString(), StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, announced.Count);
        Assert.Contains(announced, payload => payload.Replace(" ", string.Empty, StringComparison.Ordinal).Contains("[\"tr-TR\",\"en-US\"]", StringComparison.Ordinal));
        Assert.False(await StorageConnections.CreateClient(api.VideosConnectionString).GetBlobContainerClient(VideoBlobStorage.UploadsContainer)
            .GetBlobClient(track.ToString("N")).ExistsAsync(Cancellation));
    }

    [Fact]
    public async Task ADubOfAnotherLength_OrWithoutSound_Fails()
    {
        using var admin = Admin();
        var id = await ConvertedVideoAsync(admin);

        await UploadDubAsync(admin, id, "en-US", await TestVideos.CreateAudioAsync(6));
        Assert.True(await ProcessAsync());
        await UploadDubAsync(admin, id, "de-DE", await TestVideos.CreateAsync(320, 180, 3, audio: false));
        Assert.True(await ProcessAsync());

        var tracks = (await DetailsAsync(admin, id)).AudioTracks.Where(item => !item.IsOriginal).ToDictionary(item => item.Language);
        Assert.Equal(TrackStatus.Failed, tracks["en-US"].Status);
        Assert.StartsWith("The audio lasts 6", tracks["en-US"].FailureReason, StringComparison.Ordinal);
        Assert.Equal((TrackStatus.Failed, "The file has no sound to convert."), (tracks["de-DE"].Status, tracks["de-DE"].FailureReason));

        // A failed track makes room for another upload of its language.
        Assert.Equal(HttpStatusCode.Created, (await StartDubAsync(admin, id, "en-US")).StatusCode);
    }

    [Fact]
    public async Task TheRules_AreAnsweredAsProblems()
    {
        using var admin = Admin();
        var id = await ConvertedVideoAsync(admin);

        Assert.Equal((HttpStatusCode.BadRequest, "Video.LanguageUnsupported"), await ProblemAsync(await StartDubAsync(admin, id, "tr")));
        Assert.Equal((HttpStatusCode.Conflict, "Video.AudioTrackExists"), await ProblemAsync(await StartDubAsync(admin, id, "tr-TR")));
        Assert.Equal((HttpStatusCode.NotFound, "Video.NotFound"), await ProblemAsync(await StartDubAsync(admin, Guid.CreateVersion7(), "en-US")));
        Assert.Equal((HttpStatusCode.Conflict, "Video.OriginalAudioKept"), await ProblemAsync(await admin.DeleteAsync($"/admin/videos/{id}/audio-tracks/tr-TR", Cancellation)));
        Assert.Equal((HttpStatusCode.NotFound, "Video.AudioTrackNotFound"), await ProblemAsync(await admin.DeleteAsync($"/admin/videos/{id}/audio-tracks/en-US", Cancellation)));

        var ticket = await VideoApiTests.ReadAsync<AudioTrackUploadTicket>(await StartDubAsync(admin, id, "en-US"));
        Assert.Equal((HttpStatusCode.Conflict, "Video.NotUploaded"), await ProblemAsync(await admin.PostAsync($"/admin/videos/{id}/audio-tracks/{ticket.TrackId}/uploaded", null, Cancellation)));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/admin/videos/{id}/audio-tracks/en-US", Cancellation)).StatusCode);
        Assert.Single((await DetailsAsync(admin, id)).AudioTracks);

        using var reader = api.CreateClient("streaming:videos:read");
        Assert.Equal(HttpStatusCode.Forbidden, (await StartDubAsync(reader, id, "en-US")).StatusCode);
    }

    [Fact]
    public async Task AVideoConvertedBefore_CannotTakeADub()
    {
        using var admin = Admin();
        var id = await ConvertedVideoAsync(admin);
        await using (var scope = api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().Videos.Where(video => video.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(video => video.AudioLayout, AudioLayout.Muxed), Cancellation);
        }

        Assert.Equal((HttpStatusCode.Conflict, "Video.LayoutOutdated"), await ProblemAsync(await StartDubAsync(admin, id, "en-US")));
    }

    [Fact]
    public async Task ADubRemovedWhileWaiting_IsNotConverted()
    {
        using var admin = Admin();
        var id = await ConvertedVideoAsync(admin);
        var track = await UploadDubAsync(admin, id, "en-US", await TestVideos.CreateAudioAsync(3));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/admin/videos/{id}/audio-tracks/en-US", Cancellation)).StatusCode);

        Assert.False(await ProcessAsync());
        await using var scope = api.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().Set<TranscodeJob>().AnyAsync(job => job.Id == track, Cancellation));
    }

    private HttpClient Admin() => api.CreateClient("streaming:videos:read", "streaming:videos:write");

    /// <summary>A three-second video with sound, converted; the queue holds nothing else afterwards.</summary>
    private async Task<Guid> ConvertedVideoAsync(HttpClient admin, string? language = null)
    {
        var file = await TestVideos.CreateAsync(1280, 720, 3, audio: true);
        await using (var scope = api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().Set<TranscodeJob>().ExecuteDeleteAsync(Cancellation);
        }

        var response = await admin.PostAsJsonAsync("/admin/videos", new { contentKind = "Movie", contentId = Guid.CreateVersion7(), originalAudioLanguage = language }, Json, Cancellation);
        var ticket = await VideoApiTests.ReadAsync<Streaming.Application.Videos.StartVideoUpload.VideoUploadTicket>(response);
        await TestVideos.PutAsync(ticket.UploadAddress, file);
        Assert.True((await admin.PostAsync($"/admin/videos/{ticket.VideoId}/uploaded", null, Cancellation)).IsSuccessStatusCode);
        Assert.True(await ProcessAsync());
        Assert.Equal(VideoStatus.Ready, (await DetailsAsync(admin, ticket.VideoId)).Status);
        return ticket.VideoId;
    }

    private Task<HttpResponseMessage> StartDubAsync(HttpClient admin, Guid videoId, string language)
        => admin.PostAsJsonAsync($"/admin/videos/{videoId}/audio-tracks", new { language }, Json, Cancellation);

    private async Task<Guid> UploadDubAsync(HttpClient admin, Guid videoId, string language, byte[] file)
    {
        var ticket = await VideoApiTests.ReadAsync<AudioTrackUploadTicket>(await StartDubAsync(admin, videoId, language));
        await TestVideos.PutAsync(ticket.UploadAddress, file);
        var confirmed = await admin.PostAsync($"/admin/videos/{videoId}/audio-tracks/{ticket.TrackId}/uploaded", null, Cancellation);
        Assert.True(confirmed.IsSuccessStatusCode, await confirmed.Content.ReadAsStringAsync(Cancellation));
        return ticket.TrackId;
    }

    private async Task<bool> ProcessAsync()
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TranscodeProcessor>().ProcessNextAsync(Cancellation);
    }

    private static async Task<VideoDetails> DetailsAsync(HttpClient admin, Guid id)
        => await VideoApiTests.ReadAsync<VideoDetails>(await admin.GetAsync($"/admin/videos/{id}", Cancellation));

    private async Task<string> TextAsync(string path)
        => (await Videos.GetBlobClient(path).DownloadContentAsync(Cancellation)).Value.Content.ToString();

    /// <summary>Whether ffprobe finds a sound stream in a stored segment.</summary>
    private async Task<bool> HasSoundAsync(string path)
    {
        var file = Path.Combine(Path.GetTempPath(), $"netboxplus-test-{Guid.NewGuid():N}.ts");
        await Videos.GetBlobClient(path).DownloadToAsync(file, Cancellation);
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(FfmpegAvailability.FfprobePath!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in new[] { "-v", "error", "-show_entries", "stream=codec_type", "-of", "csv=p=0", file })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = System.Diagnostics.Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync(Cancellation);
            await process.WaitForExitAsync(Cancellation);
            return output.Split('\n').Any(line => line.Trim() == "audio");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Cancellation);
        return (response.StatusCode, problem?.Extensions.TryGetValue("code", out var code) == true ? code?.ToString() : null);
    }
}
