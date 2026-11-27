using Shared.Domain;

namespace Catalog.Domain.Images;

public enum ImageUse
{
    Poster,
    Backdrop,
    Photo,

    /// <summary>A title's logo: its name as designed for it, drawn over its images, so it needs transparency.</summary>
    Logo,
}

/// <summary>What an image must look like to be used.</summary>
public static class ImageRules
{
    public const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>Longest side accepted; keeps huge images declared by small files away from clients.</summary>
    public const int MaxPixels = 8000;

    /// <summary>How far the shape may be from the ideal, as a fraction of it.</summary>
    private const double AspectTolerance = 0.10;

    /// <summary>The formats that keep transparency; a logo must be one of them.</summary>
    public static readonly IReadOnlyList<string> TransparentTypes = ["image/png", "image/webp"];

    public static Result Check(ImageUse use, int width, int height, string contentType)
    {
        if (use == ImageUse.Logo && !TransparentTypes.Contains(contentType))
        {
            return ImageErrors.LogoNotTransparent;
        }

        if (width > MaxPixels || height > MaxPixels)
        {
            return ImageErrors.TooLarge;
        }

        var (minWidth, minHeight, aspect) = use switch
        {
            ImageUse.Poster => (300, 450, (double?)(2d / 3d)),
            ImageUse.Backdrop => (1280, 720, (double?)(16d / 9d)),
            // Logos come in any shape, from a wide word to a square mark.
            ImageUse.Logo => (200, 50, (double?)null),
            _ => (200, 200, (double?)null),
        };

        if (width < minWidth || height < minHeight)
        {
            return ImageErrors.TooSmall;
        }

        if (aspect is { } ideal && Math.Abs(((double)width / height) - ideal) > ideal * AspectTolerance)
        {
            return ImageErrors.WrongAspectRatio;
        }

        return Result.Success();
    }
}
