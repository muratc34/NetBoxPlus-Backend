using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Events;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Contracts;
using Streaming.Domain.Popularity;

namespace Streaming.Application.Popularity;

/// <summary>
/// Computes the titles watched most in the last seven days (today included): for each title, the
/// profiles that started it, each counted once a day. Finished days are counted once, from the sessions
/// before they are cleaned up, and kept as numbers; today is counted from the sessions every time.
/// Nothing happens when the last computation is younger than <paramref name="MinimumGap"/>, so that
/// several instances do not repeat each other's work.
/// </summary>
public sealed record ComputePopularityCommand(TimeSpan MinimumGap) : ICommand;

internal sealed class ComputePopularityHandler(
    IPopularityRepository popularity,
    IStreamingReadModel readModel,
    TimeProvider timeProvider) : ICommandHandler<ComputePopularityCommand>
{
    public async Task<Result> Handle(ComputePopularityCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var ranking = await popularity.GetAsync(cancellationToken).ConfigureAwait(false);
        if (ranking is null)
        {
            // Two first computations at once: the primary key lets one through, the other gets a conflict.
            ranking = PopularityRanking.Create();
            popularity.Add(ranking);
        }
        else if (!ranking.IsDue(now, command.MinimumGap))
        {
            return Result.Success();
        }

        var firstDay = PopularityRanking.FirstDay(today);
        var newDays = ranking.DaysToCount(today).ToList();
        var counted = newDays.Count > 0 ? newDays[0] : today;

        var scores = await readModel.DailyPlays
            .Where(plays => plays.Day >= firstDay && plays.Day < counted)
            .GroupBy(plays => plays.TitleId)
            .Select(group => new { TitleId = group.Key, Plays = group.Sum(plays => plays.Plays) })
            .ToDictionaryAsync(entry => entry.TitleId, entry => entry.Plays, cancellationToken)
            .ConfigureAwait(false);

        foreach (var day in newDays)
        {
            var plays = await CountAsync(day, cancellationToken).ConfigureAwait(false);
            popularity.AddDailyPlays(plays.Select(entry => new DailyPlays(entry.TitleId, day, entry.Score)));
            Add(scores, plays);
        }

        Add(scores, await CountAsync(today, cancellationToken).ConfigureAwait(false));
        await popularity.RemoveDailyPlaysBeforeAsync(firstDay, cancellationToken).ConfigureAwait(false);

        ranking.Update(scores.Select(entry => new TitleScore(entry.Key, entry.Value)), today, now);
        return Result.Success();
    }

    private static void Add(Dictionary<Guid, int> scores, IEnumerable<TitleScore> plays)
    {
        foreach (var entry in plays)
        {
            scores[entry.TitleId] = scores.GetValueOrDefault(entry.TitleId) + entry.Score;
        }
    }

    /// <summary>For each title started on the day (UTC), how many profiles started it.</summary>
    private async Task<List<TitleScore>> CountAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);
        var counts = await readModel.PlaybackSessions
            .Where(session => session.StartedAt >= start && session.StartedAt < end)
            .Select(session => new { session.TitleId, session.ProfileId })
            .Distinct()
            .GroupBy(play => play.TitleId)
            .Select(group => new { TitleId = group.Key, Plays = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return counts.ConvertAll(entry => new TitleScore(entry.TitleId, entry.Plays));
    }
}

// Run while the change is being saved: the outbox message commits with the ranking itself.
internal sealed class PublishPopularityUpdated(IIntegrationEventPublisher publisher, TimeProvider timeProvider) : IDomainEventHandler<PopularityChangedDomainEvent>
{
    public Task Handle(PopularityChangedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new PopularityUpdated(
                domainEvent.Revision,
                domainEvent.ComputedAt,
                [.. domainEvent.Titles.Select(title => new Contracts.PopularTitle(title.TitleId, title.Score))],
                timeProvider.GetUtcNow()),
            cancellationToken);
}
