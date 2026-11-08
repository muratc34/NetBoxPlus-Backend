using Shared.Domain;

namespace Catalog.Domain.Movies;

public static class MovieErrors
{
    public static readonly Error NotFound = Error.NotFound("Movie.NotFound", "The movie was not found.");
}
