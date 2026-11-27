using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.API.Admin;
using Catalog.Application.Browse.Search;
using Catalog.Application.Movies.GetMovie;
using Catalog.Infrastructure.Search;
using Catalog.IntegrationTests.Admin;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Paging;

namespace Catalog.IntegrationTests.Public;

/// <summary>
/// Similar and recommended titles against a real Elasticsearch. The index is shared, so every title here is
/// written with words and a genre of its own, and each test looks only at its own titles in the answer.
/// </summary>
[Collection(nameof(CatalogApiCollection))]
public sealed class RecommendationApiTests(CatalogApiFixture api)
{
    private static readonly string[] Everything =
    [
        "catalog:movies:read", "catalog:movies:write", "catalog:movies:publish", "catalog:movies:delete",
        "catalog:people:read", "catalog:people:write", "catalog:genres:read", "catalog:genres:write", "catalog:images:upload",
    ];

    private static readonly JsonSerializerOptions Json = CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Similar_SharesGenrePeopleAndWords_ClosestFirst_WithoutTheTitleItself()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var (genre, otherGenre) = (await GenreAsync(admin), await GenreAsync(admin));
        var person = await PostAsync(admin, "/admin/people", TestRequests.Person($"{Word()} {Word()}", null));
        var (a, b, c) = (Word(), Word(), Word());
        var title = await MovieAsync(admin, [genre], $"{a} {b} {c}", credit: person);
        var close = await MovieAsync(admin, [genre], $"{a} {b}", credit: person);
        var far = await MovieAsync(admin, [genre], Word());
        var unrelated = await MovieAsync(admin, [otherGenre], Word());
        await IndexAsync();

        var similar = await SimilarAsync(visitor, title);

        Assert.Equal([close, far], Mine(similar, title, close, far, unrelated));
    }

    [Fact]
    public async Task Similar_LeavesOutTitlesTooOldForTheViewer_AndKnowsNothingLikeAnUnpublishedTitle()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var word = Word();
        var title = await MovieAsync(admin, [genre], word);
        var forAll = await MovieAsync(admin, [genre], word, ageRating: "All");
        var adults = await MovieAsync(admin, [genre], word, ageRating: "Age18");
        await IndexAsync();

        Assert.Equal([forAll], Mine(await SimilarAsync(visitor, title, "&maxAgeRating=Age7"), forAll, adults));
        Assert.Equal(2, Mine(await SimilarAsync(visitor, title), forAll, adults).Count);

        var unknown = await SimilarAsync(visitor, Guid.CreateVersion7());
        Assert.Equal(0, unknown.TotalCount);
    }

    [Fact]
    public async Task Recommended_FollowsTheLiked_DropsWhatTheUnlikedShare_AndReturnsNoneOfTheGiven()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var (a, shared, u) = (Word(), Word(), Word());
        var liked = await MovieAsync(admin, [await GenreAsync(admin)], $"{a} {shared}");
        var unliked = await MovieAsync(admin, [await GenreAsync(admin)], $"{shared} {u}");
        var likeTheLiked = await MovieAsync(admin, [await GenreAsync(admin)], a);
        var likeWhatBothShare = await MovieAsync(admin, [await GenreAsync(admin)], shared);
        var seen = await MovieAsync(admin, [await GenreAsync(admin)], a);
        await IndexAsync();
        var all = new[] { liked, unliked, likeTheLiked, likeWhatBothShare, seen };

        var fromLikedOnly = await RecommendedAsync(visitor, $"like={liked}");
        Assert.Equal(new HashSet<Guid> { likeTheLiked, likeWhatBothShare, seen, unliked }, Mine(fromLikedOnly, all).ToHashSet());

        var steered = await RecommendedAsync(visitor, $"like={liked}&unlike={unliked}&exclude={seen}");
        Assert.Equal([likeTheLiked], Mine(steered, all));
    }

    [Theory]
    [InlineData("/titles/recommended", "Recommendations.LikeRequired")]
    [InlineData("/titles/recommended?unlike=01a1def6-c6b6-7861-8e7b-ff96b7664cf0", "Recommendations.LikeRequired")]
    [InlineData("/titles/recommended?like=01a1def6-c6b6-7861-8e7b-ff96b7664cf0&lang=fr", "Validation.Failed")]
    [InlineData("/titles/recommended?like=01a1def6-c6b6-7861-8e7b-ff96b7664cf0&pageSize=101", "Validation.Failed")]
    [InlineData("/titles/01a1def6-c6b6-7861-8e7b-ff96b7664cf0/similar?page=0", "Validation.Failed")]
    public async Task InvalidRequests_Are400(string path, string code)
    {
        using var visitor = api.CreateAnonymousClient();

        var response = await visitor.GetAsync(path, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TooManyIds_Are400()
    {
        using var visitor = api.CreateAnonymousClient();
        var like = string.Join('&', Enumerable.Range(0, 21).Select(_ => $"like={Guid.CreateVersion7()}"));

        var response = await visitor.GetAsync($"/titles/recommended?{like}", Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Recommendations.TooManyIds", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutTheEngine_Both_Are503()
    {
        using var visitor = api.CreateClientWithoutSearch();

        var similar = await visitor.GetAsync($"/titles/{Guid.CreateVersion7()}/similar", Cancellation);
        var recommended = await visitor.GetAsync($"/titles/recommended?like={Guid.CreateVersion7()}", Cancellation);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, similar.StatusCode);
        Assert.Contains("Search.Unavailable", await similar.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, recommended.StatusCode);
    }

    /// <summary>A word nothing else in the index has; letters only, so the analyzers keep it whole.</summary>
    private static string Word() => "zq" + string.Concat(Guid.NewGuid().ToString("N")[..10].Select(digit => (char)('a' + Convert.ToInt32(digit.ToString(), 16))));

    /// <summary>This test's titles in the answer, in its order.</summary>
    private static List<Guid> Mine(Page<SearchHit> page, params Guid[] mine)
        => [.. page.Items.Select(hit => hit.Id).Where(mine.Contains)];

    private static async Task<Page<SearchHit>> SimilarAsync(HttpClient client, Guid title, string extra = "")
        => await ReadAsync<Page<SearchHit>>(await client.GetAsync($"/titles/{title}/similar?pageSize=100{extra}", Cancellation));

    private static async Task<Page<SearchHit>> RecommendedAsync(HttpClient client, string query)
        => await ReadAsync<Page<SearchHit>>(await client.GetAsync($"/titles/recommended?{query}&pageSize=100", Cancellation));

    private async Task IndexAsync()
    {
        await using var scope = api.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SearchIndexer>().ProcessPendingAsync(Cancellation);
    }

    private static async Task<Guid> GenreAsync(HttpClient admin)
        => await PostAsync(admin, "/admin/genres", TestRequests.Genre($"tür{Guid.NewGuid():N}"));

    private async Task<Guid> MovieAsync(HttpClient admin, Guid[] genres, string synopsis, Guid? credit = null, string ageRating = "Age13")
    {
        object[] credits = credit is { } person ? [new { personId = person, role = "Actor", order = 1 }] : [];
        var body = new { translations = TestRequests.Text(Word(), synopsis), releaseYear = 2024, ageRating, runtimeMinutes = 100, genreIds = genres, credits, version = 0 };
        var movie = await PostAsync(admin, "/admin/movies", body);
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
