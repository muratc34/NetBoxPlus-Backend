namespace Catalog.Application.Abstractions;

/// <summary>Where uploaded and published image files live.</summary>
public interface IImageStorage
{
    /// <summary>A write-only address for the upload, good until <paramref name="expiresAt"/>.</summary>
    Uri CreateUploadAddress(Guid fileId, DateTimeOffset expiresAt);

    /// <summary>The size of the uploaded file, or <see langword="null"/> when nothing has arrived.</summary>
    Task<long?> GetUploadSizeAsync(Guid fileId, CancellationToken cancellationToken);

    Task<byte[]> ReadUploadAsync(Guid fileId, CancellationToken cancellationToken);

    /// <summary>Stores a checked image where customers can read it and drops the upload.</summary>
    Task PublishAsync(Guid fileId, string name, string contentType, byte[] content, CancellationToken cancellationToken);

    Task DeleteUploadAsync(Guid fileId, CancellationToken cancellationToken);

    Task DeletePublishedAsync(string name, CancellationToken cancellationToken);

    Uri PublicAddress(string name);
}

/// <summary>What a file turned out to be, whatever it was called.</summary>
public sealed record InspectedImage(string Extension, string ContentType, int Width, int Height);

/// <summary>Reads what an uploaded file really is.</summary>
public interface IImageInspector
{
    /// <summary>The file's real format and size, or <see langword="null"/> when it is not a supported image.</summary>
    Task<InspectedImage?> InspectAsync(byte[] content, CancellationToken cancellationToken);
}
