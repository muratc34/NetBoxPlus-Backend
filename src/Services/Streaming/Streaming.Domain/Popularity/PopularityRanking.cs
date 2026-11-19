using Shared.Domain;

namespace Streaming.Domain.Popularity;

/// <summary>
/// The titles watched most in the last week, and how far the counting got. There is one ranking; its
/// version is the concurrency token, so when two instances compute at the same moment only one of them
/// saves. The revision goes up only when the published list changes.
/// </summary>
public sealed class PopularityRanking : AggregateRoot
{
    /// <summary>The ranking's id: there is only one.</summary>
    public static readonly Guid SingletonId = new("01968c3e-6b2a-7c41-9f0e-5d7a1b3c4e60");

    /// <summary>How many titles the ranking keeps.</summary>
    public const int MaxTitles = 100;

    /// <summary>The days counted: today and the six before it.</summary>
    public const int WindowDays = 7;

    private readonly List<PopularTitle> _titles = [];

    private PopularityRanking(Guid id)
        : base(id)
    {
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private PopularityRanking()
    {
    }

    public IReadOnlyCollection<PopularTitle> Titles => _titles.AsReadOnly();

    /// <summary>Goes up with every published change; consumers ignore a list that is not newer.</summary>
    public int Revision { get; private set; }

    /// <summary>When the ranking was last computed, changed or not.</summary>
    public DateTimeOffset? ComputedAt { get; private set; }

    /// <summary>The last finished day whose plays were counted into <see cref="DailyPlays"/>.</summary>
    public DateOnly? CountedThrough { get; private set; }

    /// <summary>Goes up with every computation; the concurrency token.</summary>
    public int Version { get; private set; }

    public static PopularityRanking Create() => new(SingletonId);

    /// <summary>The first day of the window that ends today.</summary>
    public static DateOnly FirstDay(DateOnly today) => today.AddDays(-(WindowDays - 1));

    /// <summary>Whether the last computation is at least <paramref name="minimumGap"/> old.</summary>
    public bool IsDue(DateTimeOffset now, TimeSpan minimumGap) => ComputedAt is not { } computedAt || now - computedAt >= minimumGap;

    /// <summary>
    /// The finished days still to count: those after <see cref="CountedThrough"/>, but none before the
    /// window that ends <paramref name="today"/>.
    /// </summary>
    public IEnumerable<DateOnly> DaysToCount(DateOnly today)
    {
        var first = FirstDay(today);
        var from = CountedThrough is { } counted && counted.AddDays(1) > first ? counted.AddDays(1) : first;
        for (var day = from; day < today; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    /// <summary>
    /// Records a computation: the days before <paramref name="today"/> are counted, and the titles with
    /// the highest scores become the ranking. A ranking that comes out the same is not published again.
    /// </summary>
    public void Update(IEnumerable<TitleScore> scores, DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(scores);

        Version++;
        ComputedAt = now;
        CountedThrough = today.AddDays(-1);

        var top = scores
            .Where(score => score.Score > 0)
            .OrderByDescending(score => score.Score)
            .ThenBy(score => score.TitleId)
            .Take(MaxTitles)
            .ToList();

        var current = _titles.ToDictionary(title => title.TitleId, title => title.Score);
        if (top.Count == current.Count && top.TrueForAll(score => current.TryGetValue(score.TitleId, out var kept) && kept == score.Score))
        {
            return;
        }

        var ids = top.Select(score => score.TitleId).ToHashSet();
        _titles.RemoveAll(title => !ids.Contains(title.TitleId));
        foreach (var score in top)
        {
            if (_titles.Find(title => title.TitleId == score.TitleId) is { } title)
            {
                title.Score = score.Score;
            }
            else
            {
                _titles.Add(new PopularTitle(Id, score.TitleId, score.Score));
            }
        }

        Revision++;
        RaiseDomainEvent(new PopularityChangedDomainEvent(Revision, now, top));
    }
}

/// <summary>A title in the ranking and its score: the profiles that started it, each counted once a day.</summary>
public sealed class PopularTitle
{
    internal PopularTitle(Guid rankingId, Guid titleId, int score)
    {
        RankingId = rankingId;
        TitleId = titleId;
        Score = score;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private PopularTitle()
    {
    }

    public Guid RankingId { get; private set; }

    public Guid TitleId { get; private set; }

    public int Score { get; internal set; }
}

/// <summary>A title's score: how many profiles started it, each counted once a day.</summary>
public sealed record TitleScore(Guid TitleId, int Score);

/// <summary>
/// How many profiles started the title on a finished day. Only the number is kept: who watched is not,
/// and the sessions it was counted from are deleted a few days later.
/// </summary>
public sealed class DailyPlays
{
    public DailyPlays(Guid titleId, DateOnly day, int plays)
    {
        Ensure.NotEmpty(titleId);
        TitleId = titleId;
        Day = day;
        Plays = plays;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private DailyPlays()
    {
    }

    public Guid TitleId { get; private set; }

    public DateOnly Day { get; private set; }

    public int Plays { get; private set; }
}

/// <param name="Titles">The ranking, highest score first.</param>
public sealed record PopularityChangedDomainEvent(int Revision, DateTimeOffset ComputedAt, IReadOnlyList<TitleScore> Titles) : IDomainEvent;

public interface IPopularityRepository
{
    /// <summary>The ranking with its titles, or <see langword="null"/> before the first computation.</summary>
    Task<PopularityRanking?> GetAsync(CancellationToken cancellationToken);

    void Add(PopularityRanking ranking);

    /// <summary>Adds, with the change being saved, the counts of a finished day.</summary>
    void AddDailyPlays(IEnumerable<DailyPlays> plays);

    /// <summary>Removes, with the change being saved, the counts of days before <paramref name="day"/>.</summary>
    Task RemoveDailyPlaysBeforeAsync(DateOnly day, CancellationToken cancellationToken);
}
