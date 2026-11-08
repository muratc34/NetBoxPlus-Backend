namespace Catalog.Domain.Genres;

/// <summary>Loads and stores genres for commands.</summary>
public interface IGenreRepository
{
    Task<Genre?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether another genre already has one of this genre's names in the same language.</summary>
    Task<bool> AnyNameTakenAsync(Genre genre, CancellationToken cancellationToken);

    void Add(Genre genre);

    void Remove(Genre genre);
}
