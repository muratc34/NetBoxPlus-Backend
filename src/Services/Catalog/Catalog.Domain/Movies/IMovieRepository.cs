namespace Catalog.Domain.Movies;

/// <summary>Loads and stores movies for commands. Queries read through the application's read model instead.</summary>
public interface IMovieRepository
{
    Task<Movie?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Deleted movies filed under the genre or crediting the person, to let go of them when it is deleted.</summary>
    Task<IReadOnlyList<Movie>> ListDeletedUsingAsync(Guid? genreId, Guid? personId, CancellationToken cancellationToken);

    void Add(Movie movie);

    /// <summary>Soft-deletes the movie with the save.</summary>
    void Remove(Movie movie);
}
