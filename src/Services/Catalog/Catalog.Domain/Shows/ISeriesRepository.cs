namespace Catalog.Domain.Shows;

/// <summary>Loads and stores series for commands. Queries read through the application's read model instead.</summary>
public interface ISeriesRepository
{
    /// <summary>The series with all its seasons and episodes.</summary>
    Task<Series?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The series an episode belongs to, with all its seasons and episodes.</summary>
    Task<Series?> GetByEpisodeIdAsync(Guid episodeId, CancellationToken cancellationToken);

    /// <summary>Deleted series filed under the genre or crediting the person, to let go of them when it is deleted.</summary>
    Task<IReadOnlyList<Series>> ListDeletedUsingAsync(Guid? genreId, Guid? personId, CancellationToken cancellationToken);

    void Add(Series series);

    /// <summary>Soft-deletes the series with the save.</summary>
    void Remove(Series series);
}
