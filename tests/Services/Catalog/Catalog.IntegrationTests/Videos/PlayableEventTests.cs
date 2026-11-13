using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.API.Admin;
using Catalog.Application.Movies.GetMovie;
using Catalog.Application.Playback.AnnouncePlayability;
using Catalog.Contracts;
using Catalog.Infrastructure.Persistence;
using Catalog.IntegrationTests.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;

namespace Catalog.IntegrationTests.Videos;

/// <summary>What the streaming service is told about what can be watched, as the outbox holds it.</summary>
[Collection(nameof(CatalogApiCollection))]
public sealed class PlayableEventTests(CatalogApiFixture api)
{
    private static readonly JsonSerializerOptions Json = CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PublishingAndUnpublishing_AnnounceTheList_WithGrowingRevisions()
    {
        using var admin = api.CreateClient("catalog:movies:read", "catalog:movies:write", "catalog:movies:publish", "catalog:genres:write", "catalog:images:upload");
        var movie = await CreatePublishableMovieAsync(admin);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/admin/movies/{movie}/publish", null, Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/admin/movies/{movie}/unpublish", null, Cancellation)).StatusCode);

        var announced = await AnnouncedAsync(movie);
        Assert.Equal(2, announced.Count);
        var (published, unpublished) = (announced[0], announced[1]);
        Assert.Equal(("Movie", "Age13"), (published.Kind, published.AgeRating));
        Assert.Equal(movie, Assert.Single(published.Items).ContentId);
        Assert.Empty(unpublished.Items);
        Assert.True(unpublished.Revision > published.Revision);
    }

    [Fact]
    public async Task TheAnnouncement_RepeatsEveryTitle_AsItIsNow()
    {
        using var admin = api.CreateClient("catalog:movies:read", "catalog:movies:write", "catalog:movies:publish", "catalog:genres:write", "catalog:images:upload", "catalog:series:write");
        var movie = await CreatePublishableMovieAsync(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/admin/movies/{movie}/publish", null, Cancellation)).StatusCode);
        var published = Assert.Single(await AnnouncedAsync(movie));
        var draft = new { translations = TestRequests.Text("Taslak Dizi", "Konu."), releaseYear = 2024, ageRating = "Age7", genreIds = Array.Empty<Guid>(), credits = Array.Empty<object>(), version = 0 };
        var series = (await (await admin.PostAsJsonAsync("/admin/series", draft, Json, Cancellation)).Content.ReadFromJsonAsync<CreatedResponse>(Json, Cancellation))!.Id;

        await using (var scope = api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var titles = await context.Movies.CountAsync(Cancellation) + await context.Series.CountAsync(Cancellation);
            var count = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new AnnouncePlayabilityCommand(), Cancellation);
            Assert.True(titles > 1);
            Assert.Equal(titles, count.Value);
        }

        var again = (await AnnouncedAsync(movie))[^1];
        Assert.Equal((published.Revision, published.Items.Single()), (again.Revision, again.Items.Single()));
        var draftAnnounced = Assert.Single(await AnnouncedAsync(series));
        Assert.Equal(("Series", "Age7", 0), (draftAnnounced.Kind, draftAnnounced.AgeRating, draftAnnounced.Revision));
        Assert.Empty(draftAnnounced.Items);
    }

    private async Task<List<PlayableChanged>> AnnouncedAsync(Guid title)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var payloads = await context.OutboxMessages
            .Where(message => message.Type == "Catalog.Contracts.PlayableChanged")
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.Id)
            .Select(message => message.Payload)
            .ToListAsync(Cancellation);
        return payloads.Select(payload => JsonSerializer.Deserialize<PlayableChanged>(payload, Json)!).Where(announced => announced.TitleId == title).ToList();
    }

    private async Task<Guid> CreatePublishableMovieAsync(HttpClient admin)
    {
        var genre = (await (await admin.PostAsJsonAsync("/admin/genres", TestRequests.Genre($"g{Guid.NewGuid():N}"), Json, Cancellation)).Content.ReadFromJsonAsync<CreatedResponse>(Json, Cancellation))!.Id;
        var body = new { translations = TestRequests.Text("Oynatma Testi", "Konu."), releaseYear = 2024, ageRating = "Age13", runtimeMinutes = 90, genreIds = new[] { genre }, credits = Array.Empty<object>(), version = 0 };
        var created = await admin.PostAsJsonAsync("/admin/movies", body, Json, Cancellation);
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync(Cancellation));
        var movie = (await created.Content.ReadFromJsonAsync<CreatedResponse>(Json, Cancellation))!.Id;

        var upload = await TestImages.UploadAsync(admin, TestImages.Poster(), Json, Cancellation);
        var version = (await (await admin.GetAsync($"/admin/movies/{movie}", Cancellation)).Content.ReadFromJsonAsync<MovieDetails>(Json, Cancellation))!.Version;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/admin/movies/{movie}/images/poster", new AttachImageRequest(upload, version), Json, Cancellation)).StatusCode);
        await api.VideoReadyAsync("Movie", movie);
        return movie;
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
