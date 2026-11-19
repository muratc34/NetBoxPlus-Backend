using Catalog.Domain.Content;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catalog.Infrastructure.Search;

/// <summary>
/// Brings the search index up to date from the queue. It always indexes a title as it is now, so
/// several changes become one write and the order of the rows does not matter. A row is removed
/// only after the index has taken its title.
/// </summary>
internal sealed class SearchIndexer(CatalogDbContext context, ISearchIndexWriter index, SearchDocumentBuilder builder)
{
    private const int BatchSize = 200;

    /// <summary>Works through the queue; returns how many rows were cleared.</summary>
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var entries = context.Set<SearchIndexEntry>();
        var cleared = 0;

        await index.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

        while (true)
        {
            var batch = await entries.AsNoTracking()
                .OrderBy(entry => entry.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                return cleared;
            }

            var ids = batch.Select(entry => entry.ContentId).Distinct().ToList();
            var documents = await builder.BuildAsync(ids, cancellationToken).ConfigureAwait(false);

            await index.UpsertAsync(documents, cancellationToken).ConfigureAwait(false);
            await index.DeleteAsync([.. ids.Except(documents.Select(document => document.Id))], cancellationToken).ConfigureAwait(false);

            // By id, not by title: a change that arrived meanwhile keeps its own row for the next round.
            var rowIds = batch.Select(entry => entry.Id).ToList();
            await entries.Where(entry => rowIds.Contains(entry.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            cleared += batch.Count;
        }
    }
}

/// <summary>Runs the indexer continuously; while the index is away the queue simply waits.</summary>
internal sealed partial class SearchIndexingService(IServiceScopeFactory scopes, ILogger<SearchIndexingService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = PollInterval;

                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<SearchIndexer>().ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogFailed(logger, exception);
                    delay = RetryInterval;
                }

                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Search indexing failed; pending changes stay queued and are tried again")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>Rebuilds the search index from the database and replaces the live one at once.</summary>
internal sealed class SearchReindexer(CatalogDbContext context, ElasticSearchIndex index, SearchDocumentBuilder builder, TimeProvider timeProvider)
{
    private const int BatchSize = 500;

    public async Task<int> ReindexAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var name = await index.CreateVersionAsync(cancellationToken).ConfigureAwait(false);

        var ids = await context.Movies.AsNoTracking().Where(movie => movie.Status == PublicationStatus.Published).Select(movie => movie.Id)
            .Concat(context.Series.AsNoTracking().Where(series => series.Status == PublicationStatus.Published).Select(series => series.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var chunk in ids.Chunk(BatchSize))
        {
            var documents = await builder.BuildAsync(chunk, cancellationToken).ConfigureAwait(false);
            await index.UpsertIntoAsync(name, documents, cancellationToken).ConfigureAwait(false);
        }

        await index.SwapAliasAsync(name, cancellationToken).ConfigureAwait(false);

        // Whatever changed while the new index was filling may have gone to the old one.
        var changedMovies = await context.Movies.AsNoTracking().IgnoreQueryFilters()
            .Where(movie => movie.ModifiedAt >= started || movie.CreatedAt >= started)
            .Select(movie => movie.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var changedSeries = await context.Series.AsNoTracking().IgnoreQueryFilters()
            .Where(series => series.ModifiedAt >= started || series.CreatedAt >= started)
            .Select(series => series.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        context.Set<SearchIndexEntry>().AddRange(
            changedMovies.Select(id => Entry(id, ContentKind.Movie)).Concat(changedSeries.Select(id => Entry(id, ContentKind.Series))));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ids.Count;
    }

    private SearchIndexEntry Entry(Guid id, ContentKind kind) => new()
    {
        Id = Guid.CreateVersion7(),
        ContentId = id,
        Kind = kind,
        RequestedAt = timeProvider.GetUtcNow(),
    };
}

/// <summary>What the migrator needs without the rest of the service.</summary>
public static class SearchMaintenance
{
    /// <summary>Creates the index if there is none. Returns false when Elasticsearch cannot be reached.</summary>
    public static async Task<bool> TryEnsureIndexAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            await new ElasticSearchIndex(new Elastic.Clients.Elasticsearch.ElasticsearchClient(new Uri(url))).EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Catalog.Application.Abstractions.SearchUnavailableException)
        {
            return false;
        }
    }

    /// <summary>Rebuilds the index without interrupting search; returns how many titles it holds.</summary>
    public static Task<int> ReindexAsync(CatalogDbContext context, string url, TimeProvider timeProvider, CancellationToken cancellationToken = default)
        => new SearchReindexer(
            context,
            new ElasticSearchIndex(new Elastic.Clients.Elasticsearch.ElasticsearchClient(new Uri(url))),
            new SearchDocumentBuilder(context),
            timeProvider).ReindexAsync(cancellationToken);
}
