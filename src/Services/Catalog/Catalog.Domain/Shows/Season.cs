using Shared.Domain;

namespace Catalog.Domain.Shows;

/// <summary>Groups the episodes of a series. Part of the <see cref="Series"/> aggregate; only episodes are published.</summary>
public sealed class Season : Entity
{
    public const int MaxNumber = 1000;
    public const int TitleMaxLength = 200;

    private readonly List<Episode> _episodes = [];

    internal Season(Guid id, int number, string? title)
        : base(id)
    {
        Number = number;
        Title = title;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Season()
    {
    }

    public int Number { get; private set; }

    public string? Title { get; private set; }

    public IReadOnlyCollection<Episode> Episodes => _episodes.AsReadOnly();

    internal bool HasEpisodeNumber(int number) => _episodes.Exists(episode => episode.Number == number);

    internal void Change(int number, string? title)
    {
        Number = number;
        Title = title;
    }

    internal void Add(Episode episode) => _episodes.Add(episode);

    internal void Remove(Episode episode) => _episodes.Remove(episode);

    internal static Result<string?> CheckTitle(string? title)
    {
        var trimmed = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        return trimmed?.Length > TitleMaxLength ? SeriesErrors.SeasonTitleTooLong : Result.Success(trimmed);
    }

    internal static Result CheckNumber(int number)
        => number is >= 1 and <= MaxNumber ? Result.Success() : SeriesErrors.SeasonNumberOutOfRange;
}
