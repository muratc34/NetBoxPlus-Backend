using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.API.Admin;
using Catalog.Application.Genres.GetGenres;
using Catalog.Application.Movies.GetMovie;
using Catalog.Application.Movies.GetMovies;
using Catalog.Application.People.GetPeople;
using Catalog.Application.Shows.GetSeries;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Paging;
using Shared.Infrastructure.Persistence.Auditing;

namespace Catalog.IntegrationTests.Admin;

[Collection(nameof(CatalogApiCollection))]
public sealed class AdminApiTests(CatalogApiFixture api)
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

    [Theory]
    [InlineData("GET", "/admin/movies", "catalog:movies:read")]
    [InlineData("POST", "/admin/movies", "catalog:movies:write")]
    [InlineData("POST", "/admin/movies/01a1bbcf-1a47-7189-a777-c599de32e2de/publish", "catalog:movies:publish")]
    [InlineData("DELETE", "/admin/movies/01a1bbcf-1a47-7189-a777-c599de32e2de", "catalog:movies:delete")]
    [InlineData("GET", "/admin/series", "catalog:series:read")]
    [InlineData("POST", "/admin/series", "catalog:series:write")]
    [InlineData("POST", "/admin/series/01a1bbcf-1a47-7189-a777-c599de32e2de/publish", "catalog:series:publish")]
    [InlineData("DELETE", "/admin/series/01a1bbcf-1a47-7189-a777-c599de32e2de", "catalog:series:delete")]
    [InlineData("POST", "/admin/series/01a1bbcf-1a47-7189-a777-c599de32e2de/seasons", "catalog:series:write")]
    [InlineData("POST", "/admin/series/01a1bbcf-1a47-7189-a777-c599de32e2de/episodes/01a1bbcf-1a47-7189-a777-c599de32e2df/publish?version=0", "catalog:series:publish")]
    [InlineData("GET", "/admin/people", "catalog:people:read")]
    [InlineData("POST", "/admin/people", "catalog:people:write")]
    [InlineData("GET", "/admin/genres", "catalog:genres:read")]
    [InlineData("POST", "/admin/genres", "catalog:genres:write")]
    [InlineData("POST", "/admin/images/uploads", "catalog:images:upload")]
    [InlineData("PUT", "/admin/movies/01a1bbcf-1a47-7189-a777-c599de32e2de/images/poster", "catalog:movies:write")]
    [InlineData("PUT", "/admin/series/01a1bbcf-1a47-7189-a777-c599de32e2de/images/poster", "catalog:series:write")]
    [InlineData("PUT", "/admin/people/01a1bbcf-1a47-7189-a777-c599de32e2de/photo", "catalog:people:write")]
    public async Task Endpoints_NeedATokenAndTheirPermission(string method, string path, string permission)
    {
        using var anonymous = api.CreateAnonymousClient();
        using var unrelated = api.CreateClient(Everything.Where(item => item != permission).ToArray());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request(method, path), Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unrelated.SendAsync(Request(method, path), Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Movie_GoesFromDraftToPublishedAndBack_WithEveryStepAudited()
    {
        using var client = api.CreateClient(Everything);
        var genre = await CreateGenreAsync(client);
        var person = await CreatePersonAsync(client);

        var created = await client.PostAsJsonAsync("/admin/movies", Movie("Night Train", [], []), Json, Cancellation);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadAsync<CreatedResponse>(created)).Id;
        Assert.Equal($"/admin/movies/{id}", created.Headers.Location?.OriginalString);

        Assert.Equal("Content.GenreRequired", await CodeAsync(await client.PostAsync($"/admin/movies/{id}/publish", null, Cancellation)));

        var loaded = await GetMovieAsync(client, id);
        Assert.Equal("Draft", loaded.Status.ToString());
        var updated = await client.PutAsJsonAsync($"/admin/movies/{id}", Movie("Night Train 2", [genre], [new { personId = person, role = "Actor", characterName = "Hero", order = 1 }], loaded.Version), Json, Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

        Assert.Equal("Content.PosterRequired", await CodeAsync(await client.PostAsync($"/admin/movies/{id}/publish", null, Cancellation)));
        await AttachMoviePosterAsync(client, id);
        Assert.Equal("Content.VideoRequired", await CodeAsync(await client.PostAsync($"/admin/movies/{id}/publish", null, Cancellation)));
        await api.VideoReadyAsync("Movie", id);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/admin/movies/{id}/publish", null, Cancellation)).StatusCode);
        var published = await GetMovieAsync(client, id);
        Assert.Equal("Night Train 2", published.Title);
        Assert.Equal("Published", published.Status.ToString());
        Assert.NotNull(published.PublishedAt);
        Assert.Equal(genre, Assert.Single(published.Genres).Id);
        var credit = Assert.Single(published.Credits);
        Assert.Equal("Ada", credit.PersonName);
        Assert.Equal("Hero", credit.CharacterName);

        Assert.Equal("Content.MustBeUnpublished", await CodeAsync(await client.DeleteAsync($"/admin/movies/{id}", Cancellation)));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/admin/movies/{id}/unpublish", null, Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/admin/movies/{id}", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/movies/{id}", Cancellation)).StatusCode);

        Assert.Equal(["CreateMovie", "UpdateMovie", "AttachMovieImage", "PublishMovie", "UnpublishMovie", "DeleteMovie"], (await AuditAsync(id)).Select(entry => entry.Action));
    }

    [Fact]
    public async Task InvalidMovie_ReportsEachField()
    {
        using var client = api.CreateClient(Everything);

        var response = await client.PostAsJsonAsync("/admin/movies", new { translations = TestRequests.Text(" ", ""), releaseYear = 1000, ageRating = "All", runtimeMinutes = 0 }, Json, Cancellation);
        var codes = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("errorCodes");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Content.TitleRequired", codes.GetProperty("translations.tr.title")[0].GetString());
        Assert.Equal("Content.SynopsisRequired", codes.GetProperty("translations.tr.synopsis")[0].GetString());
        Assert.Equal("Content.ReleaseYearOutOfRange", codes.GetProperty("releaseYear")[0].GetString());
        Assert.Equal("Content.RuntimeOutOfRange", codes.GetProperty("runtimeMinutes")[0].GetString());
    }

    [Fact]
    public async Task Movie_WithUnknownGenreOrPerson_IsRejected()
    {
        using var client = api.CreateClient(Everything);

        var genre = await client.PostAsJsonAsync("/admin/movies", Movie("X", [Guid.CreateVersion7()], []), Json, Cancellation);
        var person = await client.PostAsJsonAsync("/admin/movies", Movie("X", [], [new { personId = Guid.CreateVersion7(), role = "Actor", order = 0 }]), Json, Cancellation);

        Assert.Equal("Content.UnknownGenre", await CodeAsync(genre));
        Assert.Equal("Content.UnknownPerson", await CodeAsync(person));
    }

    [Fact]
    public async Task StaleVersion_IsRefused_AndChangesNothing()
    {
        using var client = api.CreateClient(Everything);
        var id = await CreateMovieAsync(client, "Original");
        var version = (await GetMovieAsync(client, id)).Version;
        await client.PutAsJsonAsync($"/admin/movies/{id}", Movie("First editor", [], [], version), Json, Cancellation);

        var second = await client.PutAsJsonAsync($"/admin/movies/{id}", Movie("Second editor", [], [], version), Json, Cancellation);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Content.VersionConflict", await CodeAsync(second));
        Assert.Equal("First editor", (await GetMovieAsync(client, id)).Title);
    }

    [Fact]
    public async Task ChangingOnlyTheGenres_StillConflictsWithAStaleEditor()
    {
        using var client = api.CreateClient(Everything);
        var genre = await CreateGenreAsync(client);
        var id = await CreateMovieAsync(client, "Same title");
        var version = (await GetMovieAsync(client, id)).Version;
        await client.PutAsJsonAsync($"/admin/movies/{id}", Movie("Same title", [genre], [], version), Json, Cancellation);

        var stale = await client.PutAsJsonAsync($"/admin/movies/{id}", Movie("Same title", [], [], version), Json, Cancellation);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task ConcurrentEdits_OneWins_TheOthersConflict()
    {
        using var client = api.CreateClient(Everything);
        var id = await CreateMovieAsync(client, "Original");
        var version = (await GetMovieAsync(client, id)).Version;

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(index => client.PutAsJsonAsync($"/admin/movies/{id}", Movie($"Editor {index}", [], [], version), Json, Cancellation)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.All(
            responses.Where(response => response.StatusCode != HttpStatusCode.NoContent),
            response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
    }

    [Fact]
    public async Task Series_IsBuiltEpisodeByEpisode_AndPublished()
    {
        using var client = api.CreateClient(Everything);
        var genre = await CreateGenreAsync(client);
        var created = await client.PostAsJsonAsync("/admin/series", Series("Long Road", [genre]), Json, Cancellation);
        var id = (await ReadAsync<CreatedResponse>(created)).Id;

        var season = (await ReadAsync<CreatedResponse>(await client.PostAsJsonAsync($"/admin/series/{id}/seasons", TestRequests.Season(1, "First", await VersionAsync(client, id)), Json, Cancellation))).Id;
        var duplicate = await client.PostAsJsonAsync($"/admin/series/{id}/seasons", TestRequests.Season(1, null, await VersionAsync(client, id)), Json, Cancellation);
        Assert.Equal("Series.SeasonNumberTaken", await CodeAsync(duplicate));

        var episode = (await ReadAsync<CreatedResponse>(await client.PostAsJsonAsync($"/admin/series/{id}/seasons/{season}/episodes", TestRequests.Episode(1, "Pilot", "Begins.", 45, await VersionAsync(client, id)), Json, Cancellation))).Id;
        Assert.Equal("Series.PublishedEpisodeRequired", await CodeAsync(await client.PostAsync($"/admin/series/{id}/publish", null, Cancellation)));

        Assert.Equal("Content.VideoRequired", await CodeAsync(await client.PostAsync($"/admin/series/{id}/episodes/{episode}/publish?version={await VersionAsync(client, id)}", null, Cancellation)));
        await api.VideoReadyAsync("Episode", episode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/admin/series/{id}/episodes/{episode}/publish?version={await VersionAsync(client, id)}", null, Cancellation)).StatusCode);
        Assert.Equal("Content.PosterRequired", await CodeAsync(await client.PostAsync($"/admin/series/{id}/publish", null, Cancellation)));
        var poster = await TestImages.UploadAsync(client, TestImages.Poster(), Json, Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/admin/series/{id}/images/poster", new AttachImageRequest(poster, await VersionAsync(client, id)), Json, Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/admin/series/{id}/publish", null, Cancellation)).StatusCode);

        var series = await GetSeriesAsync(client, id);
        Assert.Equal("Published", series.Status.ToString());
        Assert.Equal("Published", Assert.Single(Assert.Single(series.Seasons).Episodes).Status.ToString());

        Assert.Equal("Series.PublishedEpisodeRequired", await CodeAsync(await client.PostAsync($"/admin/series/{id}/episodes/{episode}/unpublish?version={series.Version}", null, Cancellation)));
        Assert.Equal("Series.EpisodeIsPublished", await CodeAsync(await client.DeleteAsync($"/admin/series/{id}/episodes/{episode}?version={series.Version}", Cancellation)));
        Assert.Equal("Series.SeasonHasPublishedEpisodes", await CodeAsync(await client.DeleteAsync($"/admin/series/{id}/seasons/{season}?version={series.Version}", Cancellation)));

        Assert.Equal(["CreateSeries", "AddSeason", "AddEpisode", "PublishEpisode", "AttachSeriesImage", "PublishSeries"], (await AuditAsync(id)).Select(entry => entry.Action));
    }

    [Fact]
    public async Task SeasonAndEpisodeChanges_UseTheVersionOfTheSeries()
    {
        using var client = api.CreateClient(Everything);
        var id = (await ReadAsync<CreatedResponse>(await client.PostAsJsonAsync("/admin/series", Series("Road", []), Json, Cancellation))).Id;
        var version = await VersionAsync(client, id);
        var season = (await ReadAsync<CreatedResponse>(await client.PostAsJsonAsync($"/admin/series/{id}/seasons", TestRequests.Season(1, null, version), Json, Cancellation))).Id;

        var stale = await client.PostAsJsonAsync($"/admin/series/{id}/seasons/{season}/episodes", TestRequests.Episode(1, "Pilot", "Begins.", 45, version), Json, Cancellation);

        Assert.Equal("Content.VersionConflict", await CodeAsync(stale));

        var fresh = await VersionAsync(client, id);
        var episode = (await ReadAsync<CreatedResponse>(await client.PostAsJsonAsync($"/admin/series/{id}/seasons/{season}/episodes", TestRequests.Episode(1, "Pilot", "Begins.", 45, fresh), Json, Cancellation))).Id;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/admin/series/{id}/episodes/{episode}", TestRequests.Episode(2, "Renamed", "Text.", 50, await VersionAsync(client, id)), Json, Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/admin/series/{id}/seasons/{season}", TestRequests.Season(3, "Third", await VersionAsync(client, id)), Json, Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/admin/series/{id}/episodes/{episode}?version={await VersionAsync(client, id)}", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/admin/series/{id}/seasons/{season}?version={await VersionAsync(client, id)}", Cancellation)).StatusCode);
        Assert.Empty((await GetSeriesAsync(client, id)).Seasons);
    }

    [Fact]
    public async Task Lists_FilterBySearchStatusAndGenre_WithWildcardsTakenLiterally()
    {
        using var client = api.CreateClient(Everything);
        var marker = Guid.NewGuid().ToString("N")[..10];
        var genre = await CreateGenreAsync(client);
        var first = await CreateMovieAsync(client, $"{marker} one", [genre]);
        await CreateMovieAsync(client, $"{marker} two");
        await AttachMoviePosterAsync(client, first);
        await api.VideoReadyAsync("Movie", first);
        await client.PostAsync($"/admin/movies/{first}/publish", null, Cancellation);

        var byText = await ReadAsync<Page<MovieSummary>>(await client.GetAsync($"/admin/movies?search={marker.ToUpperInvariant()}&pageSize=1", Cancellation));
        var byStatus = await ReadAsync<Page<MovieSummary>>(await client.GetAsync($"/admin/movies?search={marker}&status=Published", Cancellation));
        var byGenre = await ReadAsync<Page<MovieSummary>>(await client.GetAsync($"/admin/movies?genreId={genre}", Cancellation));
        var wildcard = await ReadAsync<Page<MovieSummary>>(await client.GetAsync("/admin/movies?search=%25_%25", Cancellation));
        var tooLarge = await client.GetAsync($"/admin/movies?pageSize={PageLimits.MaxPageSize + 1}", Cancellation);

        Assert.Equal(2, byText.TotalCount);
        Assert.Single(byText.Items);
        Assert.Equal(first, Assert.Single(byStatus.Items).Id);
        Assert.Equal(first, Assert.Single(byGenre.Items).Id);
        Assert.Equal(0, wildcard.TotalCount);
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
    }

    [Fact]
    public async Task GenreInUse_CannotBeDeleted_EvenByDeletedContent()
    {
        using var client = api.CreateClient(Everything);
        var genre = await CreateGenreAsync(client);
        var movie = await CreateMovieAsync(client, "Uses it", [genre]);

        Assert.Equal("Genre.InUse", await CodeAsync(await client.DeleteAsync($"/admin/genres/{genre}", Cancellation)));

        await client.DeleteAsync($"/admin/movies/{movie}", Cancellation);
        Assert.Equal("Genre.InUse", await CodeAsync(await client.DeleteAsync($"/admin/genres/{genre}", Cancellation)));
        var listed = (await ReadAsync<List<GenreResponse>>(await client.GetAsync("/admin/genres", Cancellation))).Single(item => item.Id == genre);
        Assert.Equal(1, listed.UsageCount);
    }

    [Fact]
    public async Task PersonInUse_CannotBeDeleted_ButAnUnusedOneCan()
    {
        using var client = api.CreateClient(Everything);
        var used = await CreatePersonAsync(client);
        var unused = await CreatePersonAsync(client);
        await client.PostAsJsonAsync("/admin/series", Series("Road", [], [new { personId = used, role = "Creator", order = 0 }]), Json, Cancellation);

        Assert.Equal("Person.InUse", await CodeAsync(await client.DeleteAsync($"/admin/people/{used}", Cancellation)));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/admin/people/{unused}", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/people/{unused}", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task People_CanBeSearchedAndChanged()
    {
        using var client = api.CreateClient(Everything);
        var marker = Guid.NewGuid().ToString("N")[..10];
        var id = (await ReadAsync<CreatedResponse>(await client.PostAsJsonAsync("/admin/people", TestRequests.Person($"Ada {marker}", null), Json, Cancellation))).Id;

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/admin/people/{id}", TestRequests.Person($"Grace {marker}", "Bio"), Json, Cancellation)).StatusCode);

        var found = await ReadAsync<Page<PersonResponse>>(await client.GetAsync($"/admin/people?search={marker}", Cancellation));
        Assert.Equal($"Grace {marker}", Assert.Single(found.Items).Name);
        Assert.Equal("Person.NameRequired", await CodeAsync(await client.PostAsJsonAsync("/admin/people", TestRequests.Person(" ", null), Json, Cancellation)));
    }

    [Fact]
    public async Task GenreNames_AreUniqueByTurkishCase_AndCanBeRenamed()
    {
        using var client = api.CreateClient(Everything);
        var marker = Guid.NewGuid().ToString("N")[..10];
        var id = (await ReadAsync<CreatedResponse>(await client.PostAsJsonAsync("/admin/genres", TestRequests.Genre($"ilk {marker}"), Json, Cancellation))).Id;

        var duplicate = await client.PostAsJsonAsync("/admin/genres", TestRequests.Genre($"İLK {marker.ToUpperInvariant()}"), Json, Cancellation);

        Assert.Equal("Genre.NameTaken", await CodeAsync(duplicate));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/admin/genres/{id}", TestRequests.Genre($"ilk {marker}"), Json, Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/admin/genres/{id}", TestRequests.Genre($"son {marker}"), Json, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentGenresWithOneName_OneWins_TheOthersConflict()
    {
        using var client = api.CreateClient(Everything);
        var name = $"Tür {Guid.NewGuid():N}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => client.PostAsJsonAsync("/admin/genres", TestRequests.Genre(name), Json, Cancellation)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(
            responses.Where(response => response.StatusCode != HttpStatusCode.Created),
            response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }

    private static HttpRequestMessage Request(string method, string path)
        => new(new HttpMethod(method), path) { Content = method is "POST" or "PUT" ? JsonContent.Create(new { }) : null };

    private static object Movie(string title, Guid[] genres, object[] credits, int version = 0)
        => new { translations = TestRequests.Text(title, "A long journey."), releaseYear = 2024, ageRating = "Age13", runtimeMinutes = 120, genreIds = genres, credits, version };

    private static object Series(string title, Guid[] genres, object[]? credits = null, int version = 0)
        => new { translations = TestRequests.Text(title, "A long road."), releaseYear = 2024, ageRating = "Age16", genreIds = genres, credits = credits ?? [], version };

    private static async Task<Guid> CreateGenreAsync(HttpClient client)
        => (await ReadAsync<CreatedResponse>(await client.PostAsJsonAsync("/admin/genres", TestRequests.Genre($"Genre {Guid.NewGuid():N}"), Json, Cancellation))).Id;

    private static async Task<Guid> CreatePersonAsync(HttpClient client)
        => (await ReadAsync<CreatedResponse>(await client.PostAsJsonAsync("/admin/people", TestRequests.Person("Ada", null), Json, Cancellation))).Id;

    private static async Task<Guid> CreateMovieAsync(HttpClient client, string title, params Guid[] genres)
        => (await ReadAsync<CreatedResponse>(await client.PostAsJsonAsync("/admin/movies", Movie(title, genres, []), Json, Cancellation))).Id;

    private static async Task AttachMoviePosterAsync(HttpClient client, Guid id)
    {
        var upload = await TestImages.UploadAsync(client, TestImages.Poster(), Json, Cancellation);
        var response = await client.PutAsJsonAsync($"/admin/movies/{id}/images/poster", new AttachImageRequest(upload, (await GetMovieAsync(client, id)).Version), Json, Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<MovieDetails> GetMovieAsync(HttpClient client, Guid id)
        => await ReadAsync<MovieDetails>(await client.GetAsync($"/admin/movies/{id}", Cancellation));

    private static async Task<SeriesDetails> GetSeriesAsync(HttpClient client, Guid id)
        => await ReadAsync<SeriesDetails>(await client.GetAsync($"/admin/series/{id}", Cancellation));

    private static async Task<int> VersionAsync(HttpClient client, Guid id) => (await GetSeriesAsync(client, id)).Version;

    private async Task<List<AuditLogEntry>> AuditAsync(Guid targetId)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().AuditLogEntries.AsNoTracking()
            .Where(entry => entry.TargetId == targetId)
            .OrderBy(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(Cancellation);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancellation)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Cancellation))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("code").GetString();
}
