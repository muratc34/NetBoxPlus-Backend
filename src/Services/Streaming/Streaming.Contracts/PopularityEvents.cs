namespace Streaming.Contracts;

/// <summary>
/// The titles watched most in the last seven days changed. It carries the whole ranking, which replaces
/// the one before; a consumer ignores one whose Revision is not higher than the last it applied.
/// </summary>
/// <param name="Titles">At most 100 titles, highest score first. A title missing from it scores 0.</param>
public sealed record PopularityUpdated(int Revision, DateTimeOffset ComputedAt, IReadOnlyList<PopularTitle> Titles, DateTimeOffset OccurredAt);

/// <param name="Score">How many profiles started the title in the last seven days, each counted once a day.</param>
public sealed record PopularTitle(Guid TitleId, int Score);
