using Catalog.Application.Abstractions;
using Catalog.Domain.Content;
using Catalog.Domain.Genres;
using Catalog.Domain.People;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Events;

namespace Catalog.Application.Browse;

internal sealed class QueueChangedContent(ISearchQueue queue) : IDomainEventHandler<ContentChangedDomainEvent>
{
    public Task Handle(ContentChangedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        queue.Enqueue(domainEvent.ContentId, domainEvent.Kind);
        return Task.CompletedTask;
    }
}

/// <summary>A renamed genre shows in the documents of every published title using it.</summary>
internal sealed class QueueContentOfChangedGenre(ICatalogReadModel readModel, ISearchQueue queue) : IDomainEventHandler<GenreChangedDomainEvent>
{
    public async Task Handle(GenreChangedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var movies = await readModel.Movies
            .Where(movie => movie.Status == PublicationStatus.Published && movie.Genres.Any(genre => genre.GenreId == domainEvent.GenreId))
            .Select(movie => movie.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var series = await readModel.Series
            .Where(item => item.Status == PublicationStatus.Published && item.Genres.Any(genre => genre.GenreId == domainEvent.GenreId))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        movies.ForEach(id => queue.Enqueue(id, ContentKind.Movie));
        series.ForEach(id => queue.Enqueue(id, ContentKind.Series));
    }
}

/// <summary>A renamed person shows in the documents of every published title crediting them.</summary>
internal sealed class QueueContentOfChangedPerson(ICatalogReadModel readModel, ISearchQueue queue) : IDomainEventHandler<PersonChangedDomainEvent>
{
    public async Task Handle(PersonChangedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var movies = await readModel.Movies
            .Where(movie => movie.Status == PublicationStatus.Published && movie.Credits.Any(credit => credit.PersonId == domainEvent.PersonId))
            .Select(movie => movie.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var series = await readModel.Series
            .Where(item => item.Status == PublicationStatus.Published && item.Credits.Any(credit => credit.PersonId == domainEvent.PersonId))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        movies.ForEach(id => queue.Enqueue(id, ContentKind.Movie));
        series.ForEach(id => queue.Enqueue(id, ContentKind.Series));
    }
}
