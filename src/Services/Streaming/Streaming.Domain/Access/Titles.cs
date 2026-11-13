using Shared.Domain;

namespace Streaming.Domain.Access;

public enum TitleKind
{
    Movie,
    Series,
}

/// <summary>A movie or an episode that can be watched, and its video.</summary>
/// <param name="Sequence">Where an episode comes in its series, from the catalog; 0 for a movie.</param>
public sealed record PlayableItem(Guid ContentId, Guid VideoId, int Sequence = 0);

/// <summary>
/// What the catalog says can be watched of a movie or series, kept from its events. Each event
/// carries the whole list, so a newer one replaces what is here.
/// </summary>
public sealed class PlayableTitle : AggregateRoot
{
    private readonly List<PlayableItem> _items = [];

    private PlayableTitle(Guid id, TitleKind kind)
        : base(id)
    {
        Kind = kind;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private PlayableTitle()
    {
    }

    public TitleKind Kind { get; private set; }

    public AgeRating AgeRating { get; private set; }

    /// <summary>Empty when nothing of the title can be watched.</summary>
    public IReadOnlyCollection<PlayableItem> Items => _items.AsReadOnly();

    /// <summary>The catalog's revision of the state kept here; older news is ignored.</summary>
    public int Revision { get; private set; }

    public static PlayableTitle Create(Guid titleId, TitleKind kind) => new(titleId, kind) { Revision = -1 };

    public bool Replace(AgeRating ageRating, IEnumerable<PlayableItem> items, int revision)
    {
        ArgumentNullException.ThrowIfNull(items);

        // The same revision is the same state, told again: taken, so that an announcement carries
        // fields the contract gained since (such as the episode order) into copies made before.
        if (revision < Revision)
        {
            return false;
        }

        AgeRating = ageRating;
        _items.Clear();
        _items.AddRange(items.DistinctBy(item => item.ContentId));
        Revision = revision;
        return true;
    }
}

public interface IPlayableTitleRepository
{
    Task<PlayableTitle?> GetByIdAsync(Guid titleId, CancellationToken cancellationToken);

    void Add(PlayableTitle title);
}
