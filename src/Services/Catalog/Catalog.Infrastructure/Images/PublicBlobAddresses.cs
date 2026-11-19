using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Catalog.Infrastructure.Images;

/// <summary>Where browsers reach blob storage. Bound from the <c>BlobStorage</c> section.</summary>
public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    /// <summary>
    /// The storage endpoint as browsers see it, such as <c>http://127.0.0.1:10000/devstoreaccount1</c>,
    /// when it differs from the one in the connection string (inside containers that one is
    /// <c>http://azurite:10000/...</c>, which no browser can resolve). Unset: addresses are handed out as made.
    /// </summary>
    public Uri? PublicEndpoint { get; set; }

    internal bool IsValid() =>
        PublicEndpoint is null
        || (PublicEndpoint.IsAbsoluteUri && (PublicEndpoint.Scheme == Uri.UriSchemeHttp || PublicEndpoint.Scheme == Uri.UriSchemeHttps) && string.IsNullOrEmpty(PublicEndpoint.Query));
}

/// <summary>
/// Turns an address made by the storage client into the one browsers use: the endpoint part is
/// swapped, the rest (container, blob, SAS query) stays. A SAS signature does not cover the host, so
/// a signed address stays valid.
/// </summary>
internal sealed class PublicBlobAddresses(BlobServiceClient blobs, IOptions<BlobStorageOptions> options)
{
    private readonly string _internal = blobs.Uri.AbsoluteUri.TrimEnd('/');
    private readonly string? _public = options.Value.PublicEndpoint?.AbsoluteUri.TrimEnd('/');

    public Uri ToPublic(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (_public is null)
        {
            return address;
        }

        var text = address.AbsoluteUri;
        if (!text.StartsWith(_internal, StringComparison.Ordinal) || (text.Length > _internal.Length && text[_internal.Length] is not ('/' or '?')))
        {
            throw new InvalidOperationException("The address is not under the storage endpoint.");
        }

        return new Uri(_public + text[_internal.Length..]);
    }
}
