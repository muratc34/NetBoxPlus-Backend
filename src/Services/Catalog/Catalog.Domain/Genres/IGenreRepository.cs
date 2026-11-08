namespace Catalog.Domain.Genres;

/// <summary>Loads and stores genres for commands.</summary>
public interface IGenreRepository
{
    Task<Genre?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken);

    void Add(Genre genre);

    void Remove(Genre genre);
}
