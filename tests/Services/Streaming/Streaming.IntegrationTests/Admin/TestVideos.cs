using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using NetBoxPlus.Testing;
using Streaming.Application.Videos.StartVideoUpload;

namespace Streaming.IntegrationTests.Admin;

/// <summary>Real video files, made by ffmpeg's own test sources; nothing is kept in the repository.</summary>
internal static class TestVideos
{
    private static readonly HttpClient Storage = new();

    public static async Task<byte[]> CreateAsync(int width, int height, int seconds, bool audio)
    {
        FfmpegAvailability.SkipUnlessAvailable();
        var file = Path.Combine(Path.GetTempPath(), $"netboxplus-test-{Guid.NewGuid():N}.mp4");
        var arguments = new List<string> { "-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i", $"testsrc=size={width}x{height}:rate=24:duration={seconds}" };
        if (audio)
        {
            arguments.AddRange(["-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}", "-c:a", "aac", "-shortest"]);
        }

        arguments.AddRange(["-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", file]);
        var start = new ProcessStartInfo(FfmpegAvailability.FfmpegPath!) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        arguments.ForEach(start.ArgumentList.Add);
        using (var process = Process.Start(start)!)
        {
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await errors);
        }

        try
        {
            return await File.ReadAllBytesAsync(file);
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static async Task PutAsync(Uri address, byte[] content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, address) { Content = new ByteArrayContent(content) };
        request.Headers.Add("x-ms-blob-type", "BlockBlob");
        (await Storage.SendAsync(request)).EnsureSuccessStatusCode();
    }

    public static async Task<VideoUploadTicket> StartAsync(HttpClient admin, string kind, Guid contentId, JsonSerializerOptions json)
    {
        var response = await admin.PostAsJsonAsync("/admin/videos", new { contentKind = kind, contentId }, json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<VideoUploadTicket>(json))!;
    }
}
