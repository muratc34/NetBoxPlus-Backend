using Shared.Domain;

namespace Catalog.Domain.Genres;

public static class GenreErrors
{
    public static readonly Error NotFound = Error.NotFound("Genre.NotFound", "The genre was not found.");

    public static readonly Error NameRequired = Error.Validation("Genre.NameRequired", "The genre name is required.");

    public static readonly Error NameTooLong = Error.Validation("Genre.NameTooLong", $"The genre name must be at most {Genre.NameMaxLength} characters.");
}
