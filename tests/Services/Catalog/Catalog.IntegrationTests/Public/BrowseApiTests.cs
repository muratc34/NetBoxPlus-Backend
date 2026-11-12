using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.API.Admin;
using Catalog.Application.Browse.GetPublicPerson;
using Catalog.Application.Browse.GetPublishedMovie;
using Catalog.Application.Browse.GetPublishedSeries;
using Catalog.Application.Browse.GetTitles;
using Catalog.Application.Content;
using Catalog.Application.Movies.GetMovie;
using Catalog.Application.Shows.GetSeries;
using Catalog.IntegrationTests.Admin;
using Shared.Application.Paging;

namespace Catalog.IntegrationTests.Public;

/// <summary>The tests share a database, so each works inside a genre of its own.</summary>
[Collection(nameof(CatalogApiCollection))]
public sealed class BrowseApiTests(CatalogApiFixture api)
{
    private static readonly string[] Everything =
    [
        "catalog:movies:read", "catalog:movies:write", "catalog:movies:publish", "catalog:movies:delete",
        "catalog:series:read", "catalog:series:write", "catalog:series:publish", "catalog:series:delete",
        "catalog:people:read", "catalog:people:write", "catalog:genres:read", "catalog:genres:write",
        "catalog:images:upload",
    ];

    private static readonly JsonSerializerOptions Json = CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PublishedMovie_IsVisibleWithoutSigningIn_AndHidesAdminFields()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var person = await PersonAsync(admin);
        var movie = await MovieAsync(admin, "Night Train", genre, publish: true, person);

        var response = await visitor.GetAsync($"/movies/{movie}", Cancellation);
        var text = await response.Content.ReadAsStringAsync(Cancellation);
        var details = JsonSerializer.Deserialize<PublishedMovie>(text, Json)!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Night Train", details.Title);
        Assert.Equal(genre, Assert.Single(details.Genres).Id);
        Assert.Equal("Ada", Assert.Single(details.Credits).PersonName);
        foreach (var field in new[] { "version", "status", "createdAt", "modifiedAt", "publishedAt" })
        {
            Assert.DoesNotContain($"\"{field}\"", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task HiddenContent_IsNotListed_AndAnswers404()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var visible = await MovieAsync(admin, "Visible", genre, publish: true);
        var draft = await MovieAsync(admin, "Draft", genre, publish: false);
        var unpublished = await MovieAsync(admin, "Unpublished", genre, publish: true);
        var deleted = await MovieAsync(admin, "Deleted", genre, publish: false);
        var draftSeries = await SeriesAsync(admin, genre, publishSeries: false);
        await admin.PostAsync($"/admin/movies/{unpublished}/unpublish", null, Cancellation);
        await admin.DeleteAsync($"/admin/movies/{deleted}", Cancellation);

        var titles = await ReadAsync<Page<TitleSummary>>(await visitor.GetAsync($"/titles?genreId={genre}", Cancellation));

        Assert.Equal(visible, Assert.Single(titles.Items).Id);
        foreach (var id in new[] { draft, unpublished, deleted })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/movies/{id}", Cancellation)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/series/{draftSeries.Id}", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Series_ShowsOnlyPublishedEpisodes_AndNoEmptySeason()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var series = await SeriesAsync(admin, genre, publishSeries: true);
        await PostOkAsync(admin, $"/admin/series/{series.Id}/seasons/{series.Season}/episodes", TestRequests.Episode(2, "Hidden", "Not yet.", 45, await VersionAsync(admin, series.Id)));
        await PostOkAsync(admin, $"/admin/series/{series.Id}/seasons", TestRequests.Season(2, "Empty", await VersionAsync(admin, series.Id)));

        var details = await ReadAsync<PublishedSeries>(await visitor.GetAsync($"/series/{series.Id}", Cancellation));

        var season = Assert.Single(details.Seasons);
        Assert.Equal(1, season.Number);
        Assert.Equal("Pilot", Assert.Single(season.Episodes).Title);
    }

    [Fact]
    public async Task Titles_MixMoviesAndSeries_NewestFirst_AndPage()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var first = await MovieAsync(admin, "First", genre, publish: true);
        var second = await SeriesAsync(admin, genre, publishSeries: true);
        var third = await MovieAsync(admin, "Third", genre, publish: true);

        var pageOne = await ReadAsync<Page<TitleSummary>>(await visitor.GetAsync($"/titles?genreId={genre}&pageSize=2", Cancellation));
        var pageTwo = await ReadAsync<Page<TitleSummary>>(await visitor.GetAsync($"/titles?genreId={genre}&pageSize=2&page=2", Cancellation));

        Assert.Equal(3, pageOne.TotalCount);
        Assert.Equal([third, second.Id], pageOne.Items.Select(item => item.Id));
        Assert.Equal(TitleKind.Series, pageOne.Items[1].Kind);
        Assert.Equal("Long Road", pageOne.Items[1].Title);
        Assert.Equal(genre, Assert.Single(pageOne.Items[1].Genres).Id);
        Assert.Equal(first, Assert.Single(pageTwo.Items).Id);
    }

    [Fact]
    public async Task Titles_FilterByKindAndPerson()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var person = await PersonAsync(admin);
        var movie = await MovieAsync(admin, "With person", genre, publish: true, person);
        await MovieAsync(admin, "Without", genre, publish: true);
        var series = await SeriesAsync(admin, genre, publishSeries: true);

        var movies = await ReadAsync<Page<TitleSummary>>(await visitor.GetAsync($"/titles?genreId={genre}&kind=Movie", Cancellation));
        var onlySeries = await ReadAsync<Page<TitleSummary>>(await visitor.GetAsync($"/titles?genreId={genre}&kind=Series", Cancellation));
        var byPerson = await ReadAsync<Page<TitleSummary>>(await visitor.GetAsync($"/titles?personId={person}", Cancellation));

        Assert.Equal(2, movies.TotalCount);
        Assert.Equal(series.Id, Assert.Single(onlySeries.Items).Id);
        Assert.Equal(movie, Assert.Single(byPerson.Items).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await visitor.GetAsync("/titles?pageSize=101", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Genres_OnlyListThoseWithPublishedContent()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var used = await GenreAsync(admin);
        var draftOnly = await GenreAsync(admin);
        var viaSeries = await GenreAsync(admin);
        await MovieAsync(admin, "Published", used, publish: true);
        await MovieAsync(admin, "Draft", draftOnly, publish: false);
        await SeriesAsync(admin, viaSeries, publishSeries: true);

        var genres = await ReadAsync<List<GenreReference>>(await visitor.GetAsync("/genres", Cancellation));

        Assert.Contains(genres, genre => genre.Id == used);
        Assert.Contains(genres, genre => genre.Id == viaSeries);
        Assert.DoesNotContain(genres, genre => genre.Id == draftOnly);
    }

    [Fact]
    public async Task Person_IsVisibleOnlyWhenCreditedOnPublishedContent()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await GenreAsync(admin);
        var published = await PersonAsync(admin);
        var draftOnly = await PersonAsync(admin);
        var uncredited = await PersonAsync(admin);
        await MovieAsync(admin, "Published", genre, publish: true, published);
        await MovieAsync(admin, "Draft", genre, publish: false, draftOnly);

        Assert.Equal(published, (await ReadAsync<PublicPerson>(await visitor.GetAsync($"/people/{published}", Cancellation))).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/people/{draftOnly}", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/people/{uncredited}", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Texts_ComeInTheRequestedLanguage_AndFallBackToTurkish()
    {
        using var admin = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var genre = await PostOkAsync(admin, "/admin/genres", new GenreRequest(new Dictionary<string, string?> { ["tr"] = $"Komedi {Guid.NewGuid():N}", ["en"] = $"Comedy {Guid.NewGuid():N}" }));
        var translated = await PostOkAsync(admin, "/admin/movies", new { translations = new Dictionary<string, ContentTextFields> { ["tr"] = new("Gece Treni", "Uzun yol."), ["en"] = new("Night Train", "Long road.") }, releaseYear = 2024, ageRating = "Age13", runtimeMinutes = 120, genreIds = new[] { genre } });
        var turkishOnly = await PostOkAsync(admin, "/admin/movies", new { translations = TestRequests.Text("Sadece Türkçe", "Tek dil."), releaseYear = 2024, ageRating = "Age13", runtimeMinutes = 120, genreIds = new[] { genre } });
        await PublishMovieAsync(admin, translated);
        await PublishMovieAsync(admin, turkishOnly);

        var english = await ReadAsync<PublishedMovie>(await visitor.GetAsync($"/movies/{translated}?lang=en", Cancellation));
        var turkish = await ReadAsync<PublishedMovie>(await visitor.GetAsync($"/movies/{translated}", Cancellation));
        var fallback = await ReadAsync<PublishedMovie>(await visitor.GetAsync($"/movies/{turkishOnly}?lang=en", Cancellation));
        var titles = await ReadAsync<Page<TitleSummary>>(await visitor.GetAsync($"/titles?genreId={genre}&lang=en", Cancellation));

        Assert.Equal(("Night Train", "Long road.", "en"), (english.Title, english.Synopsis, english.Language));
        Assert.StartsWith("Comedy", Assert.Single(english.Genres).Name, StringComparison.Ordinal);
        Assert.Equal(("Gece Treni", "tr"), (turkish.Title, turkish.Language));
        Assert.StartsWith("Komedi", Assert.Single(turkish.Genres).Name, StringComparison.Ordinal);
        Assert.Equal(("Sadece Türkçe", "tr"), (fallback.Title, fallback.Language));
        Assert.Equal(["Sadece Türkçe", "Night Train"], titles.Items.Select(item => item.Title));
        Assert.Equal(["tr", "en"], titles.Items.Select(item => item.Language));
    }

    [Theory]
    [InlineData("/titles?lang=fr")]
    [InlineData("/genres?lang=fr")]
    [InlineData("/movies/01a1bbcf-1a47-7189-a777-c599de32e2de?lang=fr")]
    [InlineData("/series/01a1bbcf-1a47-7189-a777-c599de32e2de?lang=fr")]
    [InlineData("/people/01a1bbcf-1a47-7189-a777-c599de32e2de?lang=fr")]
    public async Task UnsupportedLanguage_Is400(string path)
    {
        using var visitor = api.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await visitor.GetAsync(path, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task AdminDetails_ReturnEveryLanguage()
    {
        using var admin = api.CreateClient(Everything);
        var movie = await PostOkAsync(admin, "/admin/movies", new { translations = new Dictionary<string, ContentTextFields> { ["tr"] = new("Gece Treni", "Uzun."), ["en"] = new("Night Train", "Long.") }, releaseYear = 2024, ageRating = "Age13", runtimeMinutes = 120 });

        var details = await ReadAsync<MovieDetails>(await admin.GetAsync($"/admin/movies/{movie}", Cancellation));

        Assert.Equal("Gece Treni", details.Title);
        Assert.Equal("Night Train", details.Translations["en"].Title);
    }

    [Fact]
    public async Task MissingDefaultLanguage_IsRefused()
    {
        using var admin = api.CreateClient(Everything);

        var response = await admin.PostAsJsonAsync("/admin/movies", new { translations = new Dictionary<string, ContentTextFields> { ["en"] = new("Night Train", "Long.") }, releaseYear = 2024, ageRating = "Age13", runtimeMinutes = 120 }, Json, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Content.DefaultLanguageRequired", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenreNames_AreUniquePerLanguage()
    {
        using var admin = api.CreateClient(Everything);
        var shared = $"Drama {Guid.NewGuid():N}";
        await PostOkAsync(admin, "/admin/genres", new GenreRequest(new Dictionary<string, string?> { ["tr"] = $"Dram {Guid.NewGuid():N}", ["en"] = shared }));

        var sameLanguage = await admin.PostAsJsonAsync("/admin/genres", new GenreRequest(new Dictionary<string, string?> { ["tr"] = $"Başka {Guid.NewGuid():N}", ["en"] = shared.ToUpperInvariant() }), Json, Cancellation);
        var otherLanguage = await admin.PostAsJsonAsync("/admin/genres", new GenreRequest(new Dictionary<string, string?> { ["tr"] = shared }), Json, Cancellation);

        Assert.Equal(HttpStatusCode.Conflict, sameLanguage.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherLanguage.StatusCode);
    }

    private async Task PublishMovieAsync(HttpClient admin, Guid movie)
    {
        var upload = await TestImages.UploadAsync(admin, TestImages.Poster(), Json, Cancellation);
        var version = (await ReadAsync<MovieDetails>(await admin.GetAsync($"/admin/movies/{movie}", Cancellation))).Version;
        Assert.True((await admin.PutAsJsonAsync($"/admin/movies/{movie}/images/poster", new AttachImageRequest(upload, version), Json, Cancellation)).IsSuccessStatusCode);
        await api.VideoReadyAsync("Movie", movie);
        Assert.True((await admin.PostAsync($"/admin/movies/{movie}/publish", null, Cancellation)).IsSuccessStatusCode);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static async Task<Guid> GenreAsync(HttpClient admin)
        => await PostOkAsync(admin, "/admin/genres", TestRequests.Genre($"Genre {Guid.NewGuid():N}"));

    private static async Task<Guid> PersonAsync(HttpClient admin)
        => await PostOkAsync(admin, "/admin/people", TestRequests.Person("Ada", null));

    private async Task<Guid> MovieAsync(HttpClient admin, string title, Guid genre, bool publish, Guid? person = null)
    {
        object[] credits = person is { } id ? [new { personId = id, role = "Actor", order = 1 }] : [];
        var movie = await PostOkAsync(admin, "/admin/movies", new { translations = TestRequests.Text(title, "A journey."), releaseYear = 2024, ageRating = "Age13", runtimeMinutes = 120, genreIds = new[] { genre }, credits });
        if (publish)
        {
            var upload = await TestImages.UploadAsync(admin, TestImages.Poster(), Json, Cancellation);
            var version = (await ReadAsync<MovieDetails>(await admin.GetAsync($"/admin/movies/{movie}", Cancellation))).Version;
            Assert.True((await admin.PutAsJsonAsync($"/admin/movies/{movie}/images/poster", new AttachImageRequest(upload, version), Json, Cancellation)).IsSuccessStatusCode);
            await api.VideoReadyAsync("Movie", movie);
            Assert.True((await admin.PostAsync($"/admin/movies/{movie}/publish", null, Cancellation)).IsSuccessStatusCode);
        }

        return movie;
    }

    private async Task<(Guid Id, Guid Season)> SeriesAsync(HttpClient admin, Guid genre, bool publishSeries)
    {
        var id = await PostOkAsync(admin, "/admin/series", new { translations = TestRequests.Text("Long Road", "A road."), releaseYear = 2023, ageRating = "Age16", genreIds = new[] { genre } });
        var season = await PostOkAsync(admin, $"/admin/series/{id}/seasons", TestRequests.Season(1, null, await VersionAsync(admin, id)));
        var episode = await PostOkAsync(admin, $"/admin/series/{id}/seasons/{season}/episodes", TestRequests.Episode(1, "Pilot", "Begins.", 45, await VersionAsync(admin, id)));
        await api.VideoReadyAsync("Episode", episode);
        Assert.True((await admin.PostAsync($"/admin/series/{id}/episodes/{episode}/publish?version={await VersionAsync(admin, id)}", null, Cancellation)).IsSuccessStatusCode);
        if (publishSeries)
        {
            var upload = await TestImages.UploadAsync(admin, TestImages.Poster(), Json, Cancellation);
            Assert.True((await admin.PutAsJsonAsync($"/admin/series/{id}/images/poster", new AttachImageRequest(upload, await VersionAsync(admin, id)), Json, Cancellation)).IsSuccessStatusCode);
            Assert.True((await admin.PostAsync($"/admin/series/{id}/publish", null, Cancellation)).IsSuccessStatusCode);
        }

        return (id, season);
    }

    private static async Task<int> VersionAsync(HttpClient admin, Guid id)
        => (await ReadAsync<SeriesDetails>(await admin.GetAsync($"/admin/series/{id}", Cancellation))).Version;

    private static async Task<Guid> PostOkAsync(HttpClient admin, string path, object body)
        => (await ReadAsync<CreatedResponse>(await admin.PostAsJsonAsync(path, body, Json, Cancellation))).Id;

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancellation)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Cancellation))!;
    }
}
