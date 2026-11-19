namespace Catalog.Domain.People;

/// <summary>Loads and stores people for commands.</summary>
public interface IPersonRepository
{
    Task<Person?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(Person person);

    void Remove(Person person);
}
