using Shared.Domain;

namespace Catalog.Domain.Genres;

public static class GenreErrors
{
    public static readonly Error NotFound = Error.NotFound("Genre.NotFound", "The genre was not found.");

    public static readonly Error NameTaken = Error.Conflict("Genre.NameTaken", "A genre with this name already exists.");

    public static readonly Error InUse = Error.Conflict("Genre.InUse", "The genre is used by a movie or series.");

    public static readonly Error NameRequired = Error.Validation("Genre.NameRequired", "The genre name is required.");

    public static readonly Error NameTooLong = Error.Validation("Genre.NameTooLong", $"The genre name must be at most {Genre.NameMaxLength} characters.");
}
