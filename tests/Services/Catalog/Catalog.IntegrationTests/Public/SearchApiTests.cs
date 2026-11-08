using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.API.Admin;
using Catalog.Application.Abstractions;
using Catalog.Application.Browse.GetTitles;
using Catalog.Application.Browse.Search;
using Catalog.Application.Content;
using Catalog.Application.Movies.GetMovie;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Search;
using Catalog.IntegrationTests.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Paging;

namespace Catalog.IntegrationTests.Public;

/// <summary>Against a real Elasticsearch. The tests share the index, so each searches inside a genre of its own.</summary>
[Collection(nameof(CatalogApiCollection))]
public sealed class SearchApiTests(CatalogApiFixture api)
{
    private static readonly string[] Everything =
    [
        "catalog:movies:read", "catalog:movies:write", "catalog:movies:publish", "catalog:movies:delete",
        "catalog:people:read", "catalog:people:write", "catalog:genres:read", "catalog:genres:write", "catalog:images:upload",
    ];

    private static readonly JsonSerializerOptions Json = CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Kitaplar", "kitap")]
    [InlineData("Ağaç Evi", "agac")]
    [InlineData("Işık Yılı", "ışık")]
    [InlineData("Işık Yılı", "isik")]
    [InlineData("İstanbul Geceleri", "istanbul")]
    public async Task TurkishSearch_HandlesSuffixesAccentsAndCase(string title, string query)
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var movie = await MovieAsync(admin, genre, title);
        await IndexAsync();

        Assert.Equal(movie, Assert.Single((await SearchAsync(visitor, query, genre)).Items).Id);
    }

    [Fact]
    public async Task EnglishSearch_UsesEnglishWords_AndShowsTheEnglishTitle()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var movie = await MovieAsync(admin, genre, "Gece Trenleri", english: "Running Trains");
        await IndexAsync();

        var hit = Assert.Single((await SearchAsync(visitor, "train", genre, "&lang=en")).Items);

        Assert.Equal(movie, hit.Id);
        Assert.Equal(("Running Trains", "en"), (hit.Title, hit.Language));
    }

    [Fact]
    public async Task UntranslatedTitle_IsFoundInEnglishToo_AndShownInTurkish()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        await MovieAsync(admin, genre, "Sadece Türkçe");
        await IndexAsync();

        var hit = Assert.Single((await SearchAsync(visitor, "türkçe", genre, "&lang=en")).Items);

        Assert.Equal(("Sadece Türkçe", "tr"), (hit.Title, hit.Language));
        Assert.NotNull(hit.PosterUrl);
    }

    [Fact]
    public async Task OnlyPublishedContent_IsFound()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var draft = await PostAsync(admin, "/admin/movies", MovieBody("Görünmez Film", null, genre));
        var unpublished = await MovieAsync(admin, genre, "Görünmez İkinci");
        var deleted = await MovieAsync(admin, genre, "Görünmez Üçüncü");
        await IndexAsync();
        Assert.Equal(2, (await SearchAsync(visitor, "görünmez", genre)).TotalCount);

        await admin.PostAsync($"/admin/movies/{unpublished}/unpublish", null, Cancellation);
        await admin.PostAsync($"/admin/movies/{deleted}/unpublish", null, Cancellation);
        await admin.DeleteAsync($"/admin/movies/{deleted}", Cancellation);
        await IndexAsync();

        Assert.Equal(0, (await SearchAsync(visitor, "görünmez", genre)).TotalCount);
        Assert.NotEqual(Guid.Empty, draft);
    }

    [Fact]
    public async Task ChangesReachTheIndex_AndSeveralBecomeOneWrite()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var movie = await MovieAsync(admin, genre, "Eski Ad");
        await IndexAsync();

        foreach (var title in new[] { "Ara Ad", "Yeni Ad" })
        {
            var version = (await ReadAsync<MovieDetails>(await admin.GetAsync($"/admin/movies/{movie}", Cancellation))).Version;
            await admin.PutAsJsonAsync($"/admin/movies/{movie}", MovieBody(title, null, genre, version), Json, Cancellation);
        }

        await IndexAsync();

        Assert.Equal(0, await QueueLengthAsync());
        Assert.Equal(0, (await SearchAsync(visitor, "eski", genre)).TotalCount);
        Assert.Equal("Yeni Ad", Assert.Single((await SearchAsync(visitor, "yeni", genre)).Items).Title);
    }

    [Fact]
    public async Task RenamedGenreAndPerson_UpdateTheTitlesUsingThem()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var person = await PostAsync(admin, "/admin/people", TestRequests.Person("Birinciad Soyad", null));
        await MovieAsync(admin, genre, "Künyeli Film", credit: person);
        await IndexAsync();
        Assert.Single((await SearchAsync(visitor, "birinciad", genre)).Items);

        var word = $"zz{Guid.NewGuid():N}";
        await admin.PutAsJsonAsync($"/admin/genres/{genre}", TestRequests.Genre(word), Json, Cancellation);
        await IndexAsync();

        var byGenre = Assert.Single((await SearchAsync(visitor, word, genre)).Items);
        Assert.Equal(word, Assert.Single(byGenre.Genres).Name);

        // In a step of its own: a change to the person alone would also refresh the genre names.
        await admin.PutAsJsonAsync($"/admin/people/{person}", TestRequests.Person("İkinciad Soyad", null), Json, Cancellation);
        await IndexAsync();

        Assert.Single((await SearchAsync(visitor, "ikinciad", genre)).Items);
        Assert.Empty((await SearchAsync(visitor, "birinciad", genre)).Items);
    }

    [Fact]
    public async Task Suggestions_FollowWhatIsBeingTyped()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var marker = $"qq{Guid.NewGuid():N}"[..14];
        var movie = await MovieAsync(admin, genre, $"{marker} Treni");
        await IndexAsync();

        var hits = await ReadAsync<List<SearchHit>>(await visitor.GetAsync($"/search/suggest?q={marker[..10]}", Cancellation));
        var twoWords = await ReadAsync<List<SearchHit>>(await visitor.GetAsync($"/search/suggest?q={marker}%20tre", Cancellation));

        Assert.Contains(hits, hit => hit.Id == movie);
        Assert.Contains(twoWords, hit => hit.Id == movie);
    }

    [Fact]
    public async Task Search_FiltersByKind_AndPages()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        await MovieAsync(admin, genre, "Sayfalı Bir");
        await MovieAsync(admin, genre, "Sayfalı İki");
        await MovieAsync(admin, genre, "Sayfalı Üç");
        await IndexAsync();

        var first = await SearchAsync(visitor, "sayfalı", genre, "&pageSize=2");
        var second = await SearchAsync(visitor, "sayfalı", genre, "&pageSize=2&page=2");
        var series = await SearchAsync(visitor, "sayfalı", genre, "&kind=Series");

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.DoesNotContain(second.Items[0].Id, first.Items.Select(item => item.Id));
        Assert.Equal(0, series.TotalCount);
    }

    [Theory]
    [InlineData("/search")]
    [InlineData("/search?q=%20")]
    [InlineData("/search?q=a&lang=fr")]
    [InlineData("/search?q=a&page=1000&pageSize=100")]
    [InlineData("/search/suggest")]
    [InlineData("/search/suggest?q=a&lang=fr")]
    public async Task InvalidRequests_Are400(string path)
    {
        using var visitor = api.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await visitor.GetAsync(path, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task WhileTheEngineIsDown_ChangesWait_AndNothingIsLost()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var movie = await MovieAsync(admin, genre, "Sabırlı Film");
        var waiting = await QueueLengthAsync();
        Assert.True(waiting > 0);

        await using (var scope = api.CreateScope())
        {
            var indexer = new SearchIndexer(
                scope.ServiceProvider.GetRequiredService<CatalogDbContext>(),
                new UnreachableIndex(),
                scope.ServiceProvider.GetRequiredService<SearchDocumentBuilder>());
            await Assert.ThrowsAsync<SearchUnavailableException>(() => indexer.ProcessPendingAsync(Cancellation));
        }

        Assert.Equal(waiting, await QueueLengthAsync());

        await IndexAsync();

        Assert.Equal(movie, Assert.Single((await SearchAsync(visitor, "sabırlı", genre)).Items).Id);
    }

    [Fact]
    public async Task WithoutTheEngine_SearchIs503_ButBrowsingStillWorks()
    {
        using var visitor = api.CreateClientWithoutSearch();

        var search = await visitor.GetAsync("/search?q=film", Cancellation);
        var suggest = await visitor.GetAsync("/search/suggest?q=film", Cancellation);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, search.StatusCode);
        Assert.Contains("Search.Unavailable", await search.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, suggest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/titles", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Reindex_RebuildsFromTheDatabase_AndReplacesTheIndex()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var movie = await MovieAsync(admin, genre, "Yeniden Kurulan");
        await IndexAsync();

        await using (var scope = api.CreateScope())
        {
            var client = scope.ServiceProvider.GetRequiredService<ElasticSearchIndex>();
            await client.DeleteAsync([movie], Cancellation);
        }

        Assert.Empty((await SearchAsync(visitor, "kurulan", genre)).Items);

        await using (var scope = api.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<SearchReindexer>().ReindexAsync(Cancellation) >= 1);
        }

        await IndexAsync();
        Assert.Equal(movie, Assert.Single((await SearchAsync(visitor, "kurulan", genre)).Items).Id);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private async Task IndexAsync()
    {
        await using var scope = api.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SearchIndexer>().ProcessPendingAsync(Cancellation);
    }

    private async Task<int> QueueLengthAsync()
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Set<SearchIndexEntry>().CountAsync(Cancellation);
    }

    private static async Task<Page<SearchHit>> SearchAsync(HttpClient client, string query, Guid genre, string extra = "")
        => await ReadAsync<Page<SearchHit>>(await client.GetAsync($"/search?q={Uri.EscapeDataString(query)}&genreId={genre}{extra}", Cancellation));

    private static async Task<Guid> GenreAsync(HttpClient admin)
        => await PostAsync(admin, "/admin/genres", TestRequests.Genre($"tür{Guid.NewGuid():N}"));

    private static object MovieBody(string title, string? english, Guid genre, int version = 0, Guid? credit = null)
    {
        var translations = TestRequests.Text(title, "Bir konu.");
        if (english is not null)
        {
            translations["en"] = new ContentTextFields(english, "A story.");
        }

        object[] credits = credit is { } person ? [new { personId = person, role = "Actor", order = 1 }] : [];
        return new { translations, releaseYear = 2024, ageRating = "Age13", runtimeMinutes = 120, genreIds = new[] { genre }, credits, version };
    }

    private static async Task<Guid> MovieAsync(HttpClient admin, Guid genre, string title, string? english = null, Guid? credit = null)
    {
        var movie = await PostAsync(admin, "/admin/movies", MovieBody(title, english, genre, credit: credit));
        var upload = await TestImages.UploadAsync(admin, TestImages.Poster(), Json, Cancellation);
        var version = (await ReadAsync<MovieDetails>(await admin.GetAsync($"/admin/movies/{movie}", Cancellation))).Version;
        Assert.True((await admin.PutAsJsonAsync($"/admin/movies/{movie}/images/poster", new AttachImageRequest(upload, version), Json, Cancellation)).IsSuccessStatusCode);
        Assert.True((await admin.PostAsync($"/admin/movies/{movie}/publish", null, Cancellation)).IsSuccessStatusCode);
        return movie;
    }

    private static async Task<Guid> PostAsync(HttpClient admin, string path, object body)
        => (await ReadAsync<CreatedResponse>(await admin.PostAsJsonAsync(path, body, Json, Cancellation))).Id;

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancellation)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Cancellation))!;
    }

    private sealed class UnreachableIndex : ISearchIndexWriter
    {
        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpsertAsync(IReadOnlyCollection<SearchDocument> documents, CancellationToken cancellationToken)
            => throw new SearchUnavailableException("Nothing answers.");

        public Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
            => throw new SearchUnavailableException("Nothing answers.");
    }
}
