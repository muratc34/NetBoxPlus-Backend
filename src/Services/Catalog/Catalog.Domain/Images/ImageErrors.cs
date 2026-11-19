using Shared.Domain;

namespace Catalog.Domain.Images;

public static class ImageErrors
{
    public static readonly Error UploadNotFound = Error.NotFound("Image.UploadNotFound", "The upload was not found.");

    public static readonly Error UploadAlreadyUsed = Error.Conflict("Image.UploadAlreadyUsed", "This upload has already been used.");

    public static readonly Error UploadExpired = Error.Conflict("Image.UploadExpired", "This upload has expired. Start a new one.");

    public static readonly Error NotUploaded = Error.Validation("Image.NotUploaded", "No file has been uploaded for this upload yet.");

    public static readonly Error TooLarge = Error.Validation(
        "Image.TooLarge",
        $"An image can be at most {ImageRules.MaxBytes / (1024 * 1024)} MB and {ImageRules.MaxPixels} pixels on a side.");

    public static readonly Error UnsupportedType = Error.Validation("Image.UnsupportedType", "The file is not a JPEG, PNG or WebP image.");

    public static readonly Error TooSmall = Error.Validation("Image.TooSmall", "The image has too few pixels for this use.");

    public static readonly Error WrongAspectRatio = Error.Validation("Image.WrongAspectRatio", "The image has the wrong shape for this use.");
}
