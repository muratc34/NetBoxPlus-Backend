using Catalog.Application.Abstractions;
using Catalog.Domain.Images;
using Shared.Domain;

namespace Catalog.Application.Images;

/// <summary>Turns a finished upload into a stored image, and retires the image it replaces.</summary>
internal sealed class ImageAttacher(
    IImageFileRepository files,
    IImageStorage storage,
    IImageInspector inspector,
    TimeProvider timeProvider)
{
    /// <summary>Checks the uploaded file and publishes it; the result is the image's name.</summary>
    public async Task<Result<string>> ConsumeAsync(Guid uploadId, ImageUse use, CancellationToken cancellationToken)
    {
        var file = await files.GetByIdAsync(uploadId, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return ImageErrors.UploadNotFound;
        }

        var usable = file.CheckUsable(timeProvider.GetUtcNow());
        if (usable.IsFailure)
        {
            return usable.Error;
        }

        var size = await storage.GetUploadSizeAsync(uploadId, cancellationToken).ConfigureAwait(false);
        if (size is null)
        {
            return ImageErrors.NotUploaded;
        }

        // A write address cannot limit the size, so it is checked here, before reading the file.
        if (size > ImageRules.MaxBytes)
        {
            return ImageErrors.TooLarge;
        }

        var content = await storage.ReadUploadAsync(uploadId, cancellationToken).ConfigureAwait(false);
        var image = await inspector.InspectAsync(content, cancellationToken).ConfigureAwait(false);
        if (image is null)
        {
            return ImageErrors.UnsupportedType;
        }

        var allowed = ImageRules.Check(use, image.Width, image.Height);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var name = $"{uploadId:N}.{image.Extension}";
        await storage.PublishAsync(uploadId, name, image.ContentType, content, cancellationToken).ConfigureAwait(false);
        file.Activate(name);
        return name;
    }

    /// <summary>Marks a replaced or removed image for deletion after the grace period.</summary>
    public async Task RetireAsync(string? name, CancellationToken cancellationToken)
    {
        if (name is not null && await files.GetByNameAsync(name, cancellationToken).ConfigureAwait(false) is { } file)
        {
            file.Retire(timeProvider.GetUtcNow());
        }
    }
}
