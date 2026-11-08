using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Catalog.Application.Abstractions;

namespace Catalog.Infrastructure.Images;

/// <summary>
/// Uploads land in a private container, reachable only through a short-lived write address. Only
/// files that passed the checks are written to the container customers read from.
/// </summary>
internal sealed class BlobImageStorage(BlobServiceClient blobs) : IImageStorage
{
    public const string IncomingContainer = "incoming";
    public const string ImagesContainer = "images";

    private BlobContainerClient Incoming => blobs.GetBlobContainerClient(IncomingContainer);

    private BlobContainerClient Images => blobs.GetBlobContainerClient(ImagesContainer);

    public Uri CreateUploadAddress(Guid fileId, DateTimeOffset expiresAt)
        => Incoming.GetBlobClient(UploadName(fileId)).GenerateSasUri(BlobSasPermissions.Create | BlobSasPermissions.Write, expiresAt);

    public async Task<long?> GetUploadSizeAsync(Guid fileId, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await Incoming.GetBlobClient(UploadName(fileId)).GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return properties.Value.ContentLength;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task<byte[]> ReadUploadAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var content = await Incoming.GetBlobClient(UploadName(fileId)).DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        return content.Value.Content.ToArray();
    }

    public async Task PublishAsync(Guid fileId, string name, string contentType, byte[] content, CancellationToken cancellationToken)
    {
        // The type comes from the inspection, never from the uploader; a name is never reused.
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType, CacheControl = "public, max-age=31536000, immutable" },
        };

        await Images.GetBlobClient(name).UploadAsync(BinaryData.FromBytes(content), options, cancellationToken).ConfigureAwait(false);
        await DeleteUploadAsync(fileId, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteUploadAsync(Guid fileId, CancellationToken cancellationToken)
        => Incoming.GetBlobClient(UploadName(fileId)).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public Task DeletePublishedAsync(string name, CancellationToken cancellationToken)
        => Images.GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public Uri PublicAddress(string name) => Images.GetBlobClient(name).Uri;

    private static string UploadName(Guid fileId) => fileId.ToString("N");
}
