using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Streaming.Application.Abstractions;

namespace Streaming.Infrastructure.Media;

/// <summary>
/// Uploads arrive in a private container through a short-lived write address; converted videos go
/// to another private container, read only through signed addresses.
/// </summary>
internal sealed class VideoBlobStorage(BlobServiceClient blobs) : IVideoStorage
{
    public const string UploadsContainer = "video-uploads";
    public const string VideosContainer = "videos";

    private BlobContainerClient Uploads => blobs.GetBlobContainerClient(UploadsContainer);

    private BlobContainerClient Videos => blobs.GetBlobContainerClient(VideosContainer);

    public Uri CreateUploadAddress(Guid videoId, DateTimeOffset expiresAt)
        => Uploads.GetBlobClient(UploadName(videoId)).GenerateSasUri(BlobSasPermissions.Create | BlobSasPermissions.Write, expiresAt);

    public async Task<long?> GetUploadSizeAsync(Guid videoId, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await Uploads.GetBlobClient(UploadName(videoId)).GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return properties.Value.ContentLength;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public Task DownloadUploadAsync(Guid videoId, string path, CancellationToken cancellationToken)
        => Uploads.GetBlobClient(UploadName(videoId)).DownloadToAsync(path, cancellationToken);

    public Task DeleteUploadAsync(Guid videoId, CancellationToken cancellationToken)
        => Uploads.GetBlobClient(UploadName(videoId)).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    /// <summary>Copies the converted files under the video's folder, keeping their relative paths.</summary>
    public async Task UploadOutputAsync(Guid videoId, string directory, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            var options = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = ContentTypeOf(file), CacheControl = "private, max-age=31536000, immutable" } };
            await Videos.GetBlobClient($"{FolderOf(videoId)}/{relative}").UploadAsync(file, options, cancellationToken).ConfigureAwait(false);
        }
    }

    public static string FolderOf(Guid videoId) => videoId.ToString("N");

    private static string UploadName(Guid videoId) => videoId.ToString("N");

    private static string ContentTypeOf(string file) => Path.GetExtension(file) switch
    {
        ".m3u8" => "application/vnd.apple.mpegurl",
        ".ts" => "video/mp2t",
        _ => "application/octet-stream",
    };
}

/// <summary>Creates the containers and lets the admin panel upload and players read. Safe to run again.</summary>
public static class VideoStorageSetup
{
    public static async Task EnsureAsync(
        string connectionString,
        IReadOnlyCollection<string> uploadOrigins,
        IReadOnlyCollection<string> playbackOrigins,
        CancellationToken cancellationToken = default)
    {
        var blobs = new BlobServiceClient(connectionString);
        await blobs.GetBlobContainerClient(VideoBlobStorage.UploadsContainer).CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken).ConfigureAwait(false);
        await blobs.GetBlobContainerClient(VideoBlobStorage.VideosContainer).CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken).ConfigureAwait(false);

        var rules = new List<BlobCorsRule>();
        if (uploadOrigins.Count > 0)
        {
            // Large files go up in blocks, which the browser sends as PUTs to the same address.
            rules.Add(new BlobCorsRule { AllowedOrigins = string.Join(',', uploadOrigins), AllowedMethods = "PUT", AllowedHeaders = "x-ms-blob-type,content-type,x-ms-blob-content-type", ExposedHeaders = string.Empty, MaxAgeInSeconds = 600 });
        }

        if (playbackOrigins.Count > 0)
        {
            rules.Add(new BlobCorsRule { AllowedOrigins = string.Join(',', playbackOrigins), AllowedMethods = "GET,HEAD", AllowedHeaders = "range", ExposedHeaders = "content-length,content-range", MaxAgeInSeconds = 600 });
        }

        await StorageCors.MergeAsync(blobs, rules, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// CORS rules belong to the whole storage account. Locally the services share one account, so each
/// adds its own rules and keeps everyone else's.
/// </summary>
internal static class StorageCors
{
    public static async Task MergeAsync(BlobServiceClient blobs, IReadOnlyCollection<BlobCorsRule> rules, CancellationToken cancellationToken)
    {
        if (rules.Count == 0)
        {
            return;
        }

        var properties = (await blobs.GetPropertiesAsync(cancellationToken).ConfigureAwait(false)).Value;
        var merged = properties.Cors.ToList();
        foreach (var rule in rules)
        {
            if (!merged.Exists(existing => Same(existing, rule)))
            {
                merged.Add(rule);
            }
        }

        if (merged.Count != properties.Cors.Count)
        {
            properties.Cors = merged;
            await blobs.SetPropertiesAsync(properties, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool Same(BlobCorsRule left, BlobCorsRule right)
        => left.AllowedOrigins == right.AllowedOrigins && left.AllowedMethods == right.AllowedMethods && left.AllowedHeaders == right.AllowedHeaders;
}
