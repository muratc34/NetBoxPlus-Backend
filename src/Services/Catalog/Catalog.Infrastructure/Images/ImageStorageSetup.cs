using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Catalog.Infrastructure.Images;

/// <summary>Creates the containers and lets the admin panel upload from the browser. Safe to run again.</summary>
public static class ImageStorageSetup
{
    public static async Task EnsureAsync(string connectionString, IReadOnlyCollection<string> uploadOrigins, CancellationToken cancellationToken = default)
    {
        var blobs = new BlobServiceClient(connectionString);

        await blobs.GetBlobContainerClient(BlobImageStorage.IncomingContainer)
            .CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await blobs.GetBlobContainerClient(BlobImageStorage.ImagesContainer)
            .CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (uploadOrigins.Count == 0)
        {
            return;
        }

        // CORS rules belong to the whole account, which the services share locally: add ours, keep the rest.
        var rule = new BlobCorsRule
        {
            AllowedOrigins = string.Join(',', uploadOrigins),
            AllowedMethods = "PUT",
            AllowedHeaders = "x-ms-blob-type,content-type",
            ExposedHeaders = string.Empty,
            MaxAgeInSeconds = 600,
        };
        var properties = (await blobs.GetPropertiesAsync(cancellationToken).ConfigureAwait(false)).Value;
        if (properties.Cors.Any(existing => existing.AllowedOrigins == rule.AllowedOrigins && existing.AllowedMethods == rule.AllowedMethods && existing.AllowedHeaders == rule.AllowedHeaders))
        {
            return;
        }

        properties.Cors = [.. properties.Cors, rule];
        await blobs.SetPropertiesAsync(properties, cancellationToken).ConfigureAwait(false);
    }
}
