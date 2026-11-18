using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.API.Admin;
using Catalog.Application.Browse.GetTitles;
using Catalog.Application.Browse.Search;
using Catalog.Application.Movies.GetMovie;
using Catalog.Application.Popularity;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Search;
using Catalog.IntegrationTests.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Streaming.Contracts;

namespace Catalog.IntegrationTests.Public;

/// <summary>
/// The streaming service's ranking in browsing and search. The tests share the database and the
/// index, so each works inside a genre of its own; a ranking replaces the one before it.
/// </summary>
[Collection(nameof(CatalogApiCollection))]
public sealed class PopularityApiTests(CatalogApiFixture api)
{
    private static readonly string[] Everything =
    [
        "catalog:movies:read", "catalog:movies:write", "catalog:movies:publish",
        "catalog:genres:read", "catalog:genres:write", "catalog:images:upload",
    ];

    private static readonly JsonSerializerOptions Json = CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Popular_PutsTheTitlesWatchedMostFirst_AndTheRestNewestFirst()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var first = await MovieAsync(admin, genre, "Birinci");
        var second = await MovieAsync(admin, genre, "İkinci");
        var third = await MovieAsync(admin, genre, "Üçüncü");
        var fourth = await MovieAsync(admin, genre, "Dördüncü");

        await ApplyAsync(await NextRevisionAsync(), (second, 2), (first, 5));

        Assert.Equal([first, second, fourth, third], await TitlesAsync(visitor, genre, "&sort=Popular"));
        Assert.Equal([fourth, third, second, first], await TitlesAsync(visitor, genre));
    }

    [Fact]
    public async Task AnOlderRanking_IsIgnored()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var first = await MovieAsync(admin, genre, "Eski Sıra Bir");
        var second = await MovieAsync(admin, genre, "Eski Sıra İki");
        var revision = await NextRevisionAsync();
        await ApplyAsync(revision + 1, (first, 3));

        await ApplyAsync(revision, (second, 9));

        Assert.Equal([first, second], await TitlesAsync(visitor, genre, "&sort=Popular"));
        await using var scope = api.CreateScope();
        Assert.Equal(revision + 1, (await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().PopularityCopies.SingleAsync(Cancellation)).Revision);
    }

    [Fact]
    public async Task Search_BringsPopularTitlesForward_AndFollowsTheRanking()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var first = await MovieAsync(admin, genre, "Sevilen Hikaye");
        var second = await MovieAsync(admin, genre, "Sevilen Hikaye");
        await IndexAsync();

        await ApplyAsync(await NextRevisionAsync(), (second, 50));
        await IndexAsync();
        Assert.Equal([second, first], await SearchAsync(visitor, "sevilen", genre));

        // The title that left the ranking is indexed again too: otherwise it would keep its old score.
        await ApplyAsync(await NextRevisionAsync(), (first, 50));
        await IndexAsync();
        Assert.Equal([first, second], await SearchAsync(visitor, "sevilen", genre));
    }

    [Fact]
    public async Task OnlyTitlesWhoseScoreChanged_GoToTheIndexAgain()
    {
        using var admin = api.CreateClient(Everything);
        var genre = await GenreAsync(admin);
        var (stays, changes) = (await MovieAsync(admin, genre, "Sabit"), await MovieAsync(admin, genre, "Değişen"));
        var unpublished = await PostAsync(admin, "/admin/movies", MovieBody("Yayında Değil", genre));
        await ApplyAsync(await NextRevisionAsync(), (stays, 3), (changes, 1), (unpublished, 2));
        await IndexAsync();

        await ApplyAsync(await NextRevisionAsync(), (stays, 3), (changes, 2), (unpublished, 4));

        await using var scope = api.CreateScope();
        var queued = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Set<SearchIndexEntry>()
            .Select(entry => entry.ContentId)
            .ToListAsync(Cancellation);
        Assert.Equal([changes], queued);
    }

    private async Task<int> NextRevisionAsync()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        await using var scope = api.CreateScope();
        var copy = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().PopularityCopies.SingleOrDefaultAsync(Cancellation);
        return (copy?.Revision ?? 0) + 1;
    }

    private async Task ApplyAsync(int revision, params (Guid TitleId, int Score)[] titles)
    {
        await using var scope = api.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(
            new ApplyPopularityCommand(revision, DateTimeOffset.UtcNow, [.. titles.OrderByDescending(title => title.Score).Select(title => new PopularTitle(title.TitleId, title.Score))]),
            Cancellation);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
    }

    private async Task IndexAsync()
    {
        await using var scope = api.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SearchIndexer>().ProcessPendingAsync(Cancellation);
    }

    private static async Task<List<Guid>> TitlesAsync(HttpClient client, Guid genre, string extra = "")
        => [.. (await ReadAsync<Page<TitleSummary>>(await client.GetAsync($"/titles?genreId={genre}{extra}", Cancellation))).Items.Select(title => title.Id)];

    private static async Task<List<Guid>> SearchAsync(HttpClient client, string query, Guid genre)
        => [.. (await ReadAsync<Page<SearchHit>>(await client.GetAsync($"/search?q={Uri.EscapeDataString(query)}&genreId={genre}", Cancellation))).Items.Select(hit => hit.Id)];

    private static async Task<Guid> GenreAsync(HttpClient admin)
        => await PostAsync(admin, "/admin/genres", TestRequests.Genre($"tür{Guid.NewGuid():N}"));

    private static object MovieBody(string title, Guid genre)
        => new { translations = TestRequests.Text(title, "Bir konu."), releaseYear = 2024, ageRating = "Age13", runtimeMinutes = 120, genreIds = new[] { genre }, credits = Array.Empty<object>(), version = 0 };

    private async Task<Guid> MovieAsync(HttpClient admin, Guid genre, string title)
    {
        var movie = await PostAsync(admin, "/admin/movies", MovieBody(title, genre));
        var upload = await TestImages.UploadAsync(admin, TestImages.Poster(), Json, Cancellation);
        var version = (await ReadAsync<MovieDetails>(await admin.GetAsync($"/admin/movies/{movie}", Cancellation))).Version;
        Assert.True((await admin.PutAsJsonAsync($"/admin/movies/{movie}/images/poster", new AttachImageRequest(upload, version), Json, Cancellation)).IsSuccessStatusCode);
        await api.VideoReadyAsync("Movie", movie);
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

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
