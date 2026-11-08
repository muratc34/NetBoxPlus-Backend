using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.Application.Abstractions;
using Catalog.Domain.Content;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using TransportMethod = Elastic.Transport.HttpMethod;

namespace Catalog.Infrastructure.Search;

/// <summary>What the indexing side needs; a seam so that an unreachable index can be simulated.</summary>
internal interface ISearchIndexWriter
{
    Task EnsureReadyAsync(CancellationToken cancellationToken);

    Task UpsertAsync(IReadOnlyCollection<SearchDocument> documents, CancellationToken cancellationToken);

    Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

/// <summary>
/// Talks to Elasticsearch in its own JSON: the mappings and queries read like its documentation.
/// Customers and the indexer use the alias, so that a rebuilt index can replace the old one at once.
/// </summary>
internal sealed class ElasticSearchIndex(ElasticsearchClient client) : ISearchIndex, ISearchIndexWriter
{
    public const string Alias = "catalog-titles";

    private static readonly JsonSerializerOptions Json = CreateJson();

    // Every language has an analyzer for words and one for what is being typed. Folding comes last,
    // so that "isik" finds "ışık" and stemming still sees the real letters.
    private const string Definition = """
        {
          "settings": {
            "analysis": {
              "filter": {
                "tr_lowercase": { "type": "lowercase", "language": "turkish" },
                "tr_stop": { "type": "stop", "stopwords": "_turkish_" },
                "tr_stemmer": { "type": "stemmer", "language": "turkish" },
                "en_stop": { "type": "stop", "stopwords": "_english_" },
                "en_stemmer": { "type": "stemmer", "language": "english" },
                "en_possessive": { "type": "stemmer", "language": "possessive_english" }
              },
              "analyzer": {
                "tr_text": { "tokenizer": "standard", "filter": [ "tr_lowercase", "tr_stop", "tr_stemmer", "asciifolding" ] },
                "tr_typing": { "tokenizer": "standard", "filter": [ "tr_lowercase", "asciifolding" ] },
                "en_text": { "tokenizer": "standard", "filter": [ "en_possessive", "lowercase", "en_stop", "en_stemmer", "asciifolding" ] },
                "en_typing": { "tokenizer": "standard", "filter": [ "lowercase", "asciifolding" ] }
              }
            }
          },
          "mappings": {
            "dynamic": false,
            "properties": {
              "id": { "type": "keyword" },
              "kind": { "type": "keyword" },
              "title": { "properties": { "tr": { "type": "text", "analyzer": "tr_text" }, "en": { "type": "text", "analyzer": "en_text" } } },
              "synopsis": { "properties": { "tr": { "type": "text", "analyzer": "tr_text" }, "en": { "type": "text", "analyzer": "en_text" } } },
              "genres": { "properties": { "tr": { "type": "text", "analyzer": "tr_text" }, "en": { "type": "text", "analyzer": "en_text" } } },
              "suggest": { "properties": { "tr": { "type": "search_as_you_type", "analyzer": "tr_typing" }, "en": { "type": "search_as_you_type", "analyzer": "en_typing" } } },
              "originalTitle": { "type": "text", "analyzer": "tr_typing" },
              "people": { "type": "text", "analyzer": "tr_typing" },
              "genreIds": { "type": "keyword" },
              "publishedAt": { "type": "date" }
            }
          }
        }
        """;

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        var (status, _) = await SendAsync(TransportMethod.GET, $"/_alias/{Alias}", null, cancellationToken, allowNotFound: true).ConfigureAwait(false);
        if (status == 404)
        {
            var name = await CreateVersionAsync(cancellationToken).ConfigureAwait(false);
            await SwapAliasAsync(name, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A new empty index, not yet in use; returns its name.</summary>
    public async Task<string> CreateVersionAsync(CancellationToken cancellationToken)
    {
        var name = $"{Alias}-{Guid.CreateVersion7():N}";
        await SendAsync(TransportMethod.PUT, $"/{name}", Definition, cancellationToken).ConfigureAwait(false);
        return name;
    }

    /// <summary>Points the alias at the index in one step and drops the index it pointed at before.</summary>
    public async Task SwapAliasAsync(string index, CancellationToken cancellationToken)
    {
        var (status, body) = await SendAsync(TransportMethod.GET, $"/_alias/{Alias}", null, cancellationToken, allowNotFound: true).ConfigureAwait(false);

        var previous = new List<string>();
        if (status != 404)
        {
            using var document = JsonDocument.Parse(body);
            previous.AddRange(document.RootElement.EnumerateObject().Select(item => item.Name));
        }

        var actions = previous.Select(name => (object)new { remove = new { index = name, alias = Alias } })
            .Append(new { add = new { index, alias = Alias } });
        await SendAsync(TransportMethod.POST, "/_aliases", JsonSerializer.Serialize(new { actions }), cancellationToken).ConfigureAwait(false);

        foreach (var name in previous.Where(name => name != index))
        {
            await SendAsync(TransportMethod.DELETE, $"/{name}", null, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task UpsertAsync(IReadOnlyCollection<SearchDocument> documents, CancellationToken cancellationToken)
        => UpsertIntoAsync(Alias, documents, cancellationToken);

    public async Task UpsertIntoAsync(string index, IReadOnlyCollection<SearchDocument> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return;
        }

        var body = new StringBuilder();
        foreach (var document in documents)
        {
            body.Append(CultureInfo.InvariantCulture, $$$"""{"index":{"_id":"{{{document.Id}}}"}}""").Append('\n');
            body.Append(JsonSerializer.Serialize(document, Json)).Append('\n');
        }

        await BulkAsync(index, body.ToString(), cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var body = new StringBuilder();
        foreach (var id in ids)
        {
            body.Append(CultureInfo.InvariantCulture, $$$"""{"delete":{"_id":"{{{id}}}"}}""").Append('\n');
        }

        await BulkAsync(Alias, body.ToString(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<SearchResults> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var fields = new List<string> { $"title.{query.Language}^4", $"genres.{query.Language}^2", $"synopsis.{query.Language}", "originalTitle^3", "people^2" };
        if (query.Language != Languages.Default)
        {
            fields.AddRange([$"title.{Languages.Default}^2", $"genres.{Languages.Default}", $"synopsis.{Languages.Default}"]);
        }

        var filters = new List<object>();
        if (query.Kind is { } kind)
        {
            filters.Add(new { term = new { kind = kind.ToString() } });
        }

        if (query.GenreId is { } genreId)
        {
            filters.Add(new { term = new { genreIds = genreId.ToString() } });
        }

        var request = new
        {
            from = query.Offset,
            size = query.Size,
            track_total_hits = true,
            query = new
            {
                @bool = new
                {
                    must = new object[] { new { multi_match = new { query = query.Text, fields, type = "best_fields" } } },
                    filter = filters,
                },
            },
        };

        var (documents, total) = await RunSearchAsync(request, cancellationToken).ConfigureAwait(false);
        return new SearchResults(total, documents);
    }

    public async Task<IReadOnlyList<SearchDocument>> SuggestAsync(string text, string language, int size, CancellationToken cancellationToken)
    {
        var fields = new List<string>();
        foreach (var candidate in new[] { language, Languages.Default }.Distinct(StringComparer.Ordinal))
        {
            fields.AddRange([$"suggest.{candidate}", $"suggest.{candidate}._2gram", $"suggest.{candidate}._3gram"]);
        }

        var request = new { size, query = new { multi_match = new { query = text, type = "bool_prefix", fields } } };

        var (documents, _) = await RunSearchAsync(request, cancellationToken).ConfigureAwait(false);
        return documents;
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private async Task<(IReadOnlyList<SearchDocument> Documents, long Total)> RunSearchAsync(object request, CancellationToken cancellationToken)
    {
        var (_, body) = await SendAsync(TransportMethod.POST, $"/{Alias}/_search", JsonSerializer.Serialize(request), cancellationToken).ConfigureAwait(false);

        using var response = JsonDocument.Parse(body);
        var hits = response.RootElement.GetProperty("hits");
        var total = hits.GetProperty("total").GetProperty("value").GetInt64();
        var documents = hits.GetProperty("hits").EnumerateArray()
            .Select(hit => hit.GetProperty("_source").Deserialize<SearchDocument>(Json)!)
            .ToList();

        return (documents, total);
    }

    // wait_for: the indexer works in the background, and a finished batch can then be searched.
    private async Task BulkAsync(string index, string body, CancellationToken cancellationToken)
    {
        var (_, response) = await SendAsync(TransportMethod.POST, $"/{index}/_bulk?refresh=wait_for", body, cancellationToken).ConfigureAwait(false);

        using var result = JsonDocument.Parse(response);
        if (result.RootElement.GetProperty("errors").GetBoolean() && HasRealError(result.RootElement))
        {
            throw new SearchUnavailableException("The search index refused part of a batch.");
        }
    }

    // Deleting a document that is not there is reported as an item-level 404: already the wanted state.
    private static bool HasRealError(JsonElement result)
        => result.GetProperty("items").EnumerateArray().Any(item => item.EnumerateObject().Single().Value.GetProperty("status").GetInt32() is >= 300 and not 404);

    private async Task<(int Status, string Body)> SendAsync(TransportMethod method, string path, string? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        var response = body is null
            ? await client.Transport.RequestAsync<StringResponse>(method, path, cancellationToken).ConfigureAwait(false)
            : await client.Transport.RequestAsync<StringResponse>(method, path, PostData.String(body), cancellationToken).ConfigureAwait(false);

        var status = response.ApiCallDetails.HttpStatusCode;
        if (status is >= 200 and < 300 || (allowNotFound && status == 404))
        {
            return (status.Value, response.Body);
        }

        throw new SearchUnavailableException($"Elasticsearch answered {method} {path} with {status?.ToString(CultureInfo.InvariantCulture) ?? "no response"}.", response.ApiCallDetails.OriginalException);
    }
}
