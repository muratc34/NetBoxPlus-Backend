namespace Catalog.Domain.Movies;

/// <summary>Loads and stores movies for commands. Queries read through the application's read model instead.</summary>
public interface IMovieRepository
{
    Task<Movie?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(Movie movie);

    /// <summary>Soft-deletes the movie with the save.</summary>
    void Remove(Movie movie);
}
