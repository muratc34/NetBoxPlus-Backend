namespace Catalog.Domain.Content;

/// <summary>The images a movie or series can show.</summary>
public enum ContentImageSlot
{
    Poster,
    Backdrop,

    /// <summary>The title's name as designed for it, drawn over its images; optional.</summary>
    Logo,
}
