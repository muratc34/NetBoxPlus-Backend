using Shared.Domain;

namespace Catalog.Domain.Popularity;

/// <summary>
/// The streaming service's ranking of the titles watched most in the last week. There is one copy; a
/// ranking replaces it only when its revision is higher, and the revision is the concurrency token, so
/// two rankings applied at once cannot both win.
/// </summary>
public sealed class PopularityCopy : AggregateRoot
{
    /// <summary>The copy's id: there is only one.</summary>
    public static readonly Guid SingletonId = new("01968c3e-6b2a-7c41-9f0e-5d7a1b3c4e61");

    private readonly List<TitlePopularity> _titles = [];

    private PopularityCopy(Guid id)
        : base(id)
    {
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private PopularityCopy()
    {
    }

    public IReadOnlyCollection<TitlePopularity> Titles => _titles.AsReadOnly();

    /// <summary>The revision of the ranking applied last; the concurrency token.</summary>
    public int Revision { get; private set; }

    public DateTimeOffset? ComputedAt { get; private set; }

    public static PopularityCopy Create() => new(SingletonId);

    /// <summary>
    /// Replaces the ranking with a newer one; an older or the same revision changes nothing. Returns
    /// the titles whose score changed, those that left the ranking included.
    /// </summary>
    public IReadOnlyCollection<Guid> Apply(int revision, DateTimeOffset computedAt, IReadOnlyCollection<(Guid TitleId, int Score)> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);

        if (revision <= Revision)
        {
            return [];
        }

        Revision = revision;
        ComputedAt = computedAt;

        var scores = titles.Where(title => title.Score > 0).GroupBy(title => title.TitleId).ToDictionary(group => group.Key, group => group.Max(title => title.Score));
        var changed = _titles.Where(title => !scores.ContainsKey(title.TitleId)).Select(title => title.TitleId).ToList();
        _titles.RemoveAll(title => !scores.ContainsKey(title.TitleId));

        foreach (var (titleId, score) in scores)
        {
            if (_titles.Find(title => title.TitleId == titleId) is { } title)
            {
                if (title.Score != score)
                {
                    title.Score = score;
                    changed.Add(titleId);
                }
            }
            else
            {
                _titles.Add(new TitlePopularity(Id, titleId, score));
                changed.Add(titleId);
            }
        }

        return changed;
    }
}

/// <summary>A title's score in the ranking: how many profiles started it in the last week, each once a day.</summary>
public sealed class TitlePopularity
{
    internal TitlePopularity(Guid copyId, Guid titleId, int score)
    {
        CopyId = copyId;
        TitleId = titleId;
        Score = score;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private TitlePopularity()
    {
    }

    public Guid CopyId { get; private set; }

    public Guid TitleId { get; private set; }

    public int Score { get; internal set; }
}

public interface IPopularityRepository
{
    /// <summary>The copy with its titles, or <see langword="null"/> before the first ranking.</summary>
    Task<PopularityCopy?> GetAsync(CancellationToken cancellationToken);

    void Add(PopularityCopy copy);
}
