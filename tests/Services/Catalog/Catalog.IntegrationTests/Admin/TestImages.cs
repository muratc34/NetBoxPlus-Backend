using System.Net.Http.Json;
using System.Text.Json;
using Catalog.Application.Images.RequestImageUpload;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Catalog.IntegrationTests.Admin;

/// <summary>Real image files, and the upload a browser would do: straight to storage, not through the API.</summary>
internal static class TestImages
{
    private static readonly HttpClient Storage = new();

    public static byte[] Png(int width, int height) => Encode(width, height, (image, stream) => image.SaveAsPng(stream));

    public static byte[] Jpeg(int width, int height) => Encode(width, height, (image, stream) => image.SaveAsJpeg(stream));

    public static byte[] Webp(int width, int height) => Encode(width, height, (image, stream) => image.SaveAsWebp(stream));

    /// <summary>An image, but not a supported kind.</summary>
    public static byte[] Gif(int width, int height) => Encode(width, height, (image, stream) => image.SaveAsGif(stream));

    public static byte[] Poster() => Png(300, 450);

    private static byte[] Encode(int width, int height, Action<Image<Rgba32>, MemoryStream> save)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        save(image, stream);
        return stream.ToArray();
    }

    public static async Task<Guid> UploadAsync(HttpClient client, byte[] content, JsonSerializerOptions json, CancellationToken cancellationToken)
    {
        var response = await client.PostAsync("/admin/images/uploads", null, cancellationToken);
        var ticket = (await response.Content.ReadFromJsonAsync<ImageUploadTicket>(json, cancellationToken))!;
        await PutAsync(ticket.UploadAddress, content, cancellationToken);
        return ticket.UploadId;
    }

    public static async Task PutAsync(Uri address, byte[] content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, address) { Content = new ByteArrayContent(content) };
        request.Headers.Add("x-ms-blob-type", "BlockBlob");
        (await Storage.SendAsync(request, cancellationToken)).EnsureSuccessStatusCode();
    }
}
