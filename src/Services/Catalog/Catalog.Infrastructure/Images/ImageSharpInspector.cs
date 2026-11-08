using Catalog.Application.Abstractions;
using SixLabors.ImageSharp;

namespace Catalog.Infrastructure.Images;

/// <summary>
/// Identifies a file from its contents. Only the header is read, so a small file claiming enormous
/// dimensions costs nothing here.
/// </summary>
internal sealed class ImageSharpInspector : IImageInspector
{
    private static readonly Dictionary<string, (string Extension, string ContentType)> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JPEG"] = ("jpg", "image/jpeg"),
        ["PNG"] = ("png", "image/png"),
        ["WEBP"] = ("webp", "image/webp"),
    };

    public async Task<InspectedImage?> InspectAsync(byte[] content, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);

        try
        {
            var info = await Image.IdentifyAsync(stream, cancellationToken).ConfigureAwait(false);
            var format = info.Metadata.DecodedImageFormat;

            return format is not null && Supported.TryGetValue(format.Name, out var known)
                ? new InspectedImage(known.Extension, known.ContentType, info.Width, info.Height)
                : null;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            // The file is untrusted and the reader fails on it in many ways: all of them mean "not an image".
            return null;
        }
    }
}
