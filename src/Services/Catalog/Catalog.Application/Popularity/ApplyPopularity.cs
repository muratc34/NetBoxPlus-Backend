using Catalog.Application.Abstractions;
using Catalog.Domain.Content;
using Catalog.Domain.Popularity;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;
using Streaming.Contracts;

namespace Catalog.Application.Popularity;

/// <summary>
/// Keeps the streaming service's ranking of the titles watched most. A ranking that is not newer than
/// the one kept is ignored. The published titles whose score changed go to the search index again.
/// </summary>
public sealed record ApplyPopularityCommand(int Revision, DateTimeOffset ComputedAt, IReadOnlyList<PopularTitle> Titles) : ICommand;

internal sealed class ApplyPopularityHandler(IPopularityRepository popularity, ICatalogReadModel readModel, ISearchQueue queue) : ICommandHandler<ApplyPopularityCommand>
{
    public async Task<Result> Handle(ApplyPopularityCommand command, CancellationToken cancellationToken)
    {
        var copy = await popularity.GetAsync(cancellationToken).ConfigureAwait(false);
        if (copy is null)
        {
            // Two first rankings at once: the primary key lets one through, the other is tried again.
            copy = PopularityCopy.Create();
            popularity.Add(copy);
        }

        var changed = copy.Apply(command.Revision, command.ComputedAt, [.. command.Titles.Select(title => (title.TitleId, title.Score))]);
        if (changed.Count == 0)
        {
            return Result.Success();
        }

        var movies = await readModel.Movies
            .Where(movie => changed.Contains(movie.Id) && movie.Status == PublicationStatus.Published)
            .Select(movie => movie.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var series = await readModel.Series
            .Where(item => changed.Contains(item.Id) && item.Status == PublicationStatus.Published)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        movies.ForEach(id => queue.Enqueue(id, ContentKind.Movie));
        series.ForEach(id => queue.Enqueue(id, ContentKind.Series));
        return Result.Success();
    }
}

/// <summary>
/// Rankings from the streaming service. The full type name is this handler's inbox key: renaming or
/// moving it would process the messages it already handled again.
/// </summary>
internal sealed class ApplyPopularityOnPopularityUpdated(IDispatcher dispatcher) : IIntegrationEventHandler<PopularityUpdated>
{
    public Task<Result> Handle(PopularityUpdated integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new ApplyPopularityCommand(integrationEvent.Revision, integrationEvent.ComputedAt, integrationEvent.Titles), cancellationToken);
}
