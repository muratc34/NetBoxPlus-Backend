namespace Catalog.Domain.Images;

/// <summary>Loads and stores image files for commands.</summary>
public interface IImageFileRepository
{
    Task<ImageFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<ImageFile?> GetByNameAsync(string name, CancellationToken cancellationToken);

    void Add(ImageFile file);
}
