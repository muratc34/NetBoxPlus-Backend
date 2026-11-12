using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.API.Admin;
using Catalog.Application.Movies.GetMovie;
using Catalog.Domain.Content;
using Catalog.IntegrationTests.Admin;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Streaming.Contracts;

namespace Catalog.IntegrationTests.Videos;

/// <summary>The streaming service's video events, through the inbox as the messaging layer delivers them.</summary>
[Collection(nameof(CatalogApiCollection))]
public sealed class VideoEventTests(CatalogApiFixture api)
{
    private static readonly JsonSerializerOptions Json = CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task VideoEvents_ReachTheMovie_OnceEach()
    {
        using var admin = api.CreateClient("catalog:movies:read", "catalog:movies:write", "catalog:genres:write");
        var movie = await CreateMovieAsync(admin);
        var video = Guid.CreateVersion7();
        var queued = Guid.NewGuid();

        Assert.True(await DeliverAsync(queued, new VideoQueued(video, "Movie", movie, 1, DateTimeOffset.UtcNow)));
        Assert.Equal((VideoState.Processing, false), await StateAsync(admin, movie));

        Assert.True(await DeliverAsync(Guid.NewGuid(), new VideoReady(video, "Movie", movie, 42.5, 2, DateTimeOffset.UtcNow)));
        Assert.Equal((VideoState.Ready, true), await StateAsync(admin, movie));

        Assert.False(await DeliverAsync(queued, new VideoQueued(video, "Movie", movie, 1, DateTimeOffset.UtcNow)));
        Assert.True(await DeliverAsync(Guid.NewGuid(), new VideoQueued(video, "Movie", movie, 1, DateTimeOffset.UtcNow)));
        Assert.Equal((VideoState.Ready, true), await StateAsync(admin, movie));
    }

    [Fact]
    public async Task AFailedNewUpload_KeepsTheMoviePlayable()
    {
        using var admin = api.CreateClient("catalog:movies:read", "catalog:movies:write", "catalog:genres:write");
        var movie = await CreateMovieAsync(admin);
        var first = Guid.CreateVersion7(DateTimeOffset.UtcNow);
        var second = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddSeconds(1));
        await DeliverAsync(Guid.NewGuid(), new VideoReady(first, "Movie", movie, 10, 2, DateTimeOffset.UtcNow));

        await DeliverAsync(Guid.NewGuid(), new VideoFailed(second, "Movie", movie, "The file is not a readable video.", 2, DateTimeOffset.UtcNow));

        Assert.Equal((VideoState.Failed, true), await StateAsync(admin, movie));
    }

    [Fact]
    public async Task EventsForUnknownContent_AreAccepted_AndIgnored()
    {
        Assert.True(await DeliverAsync(Guid.NewGuid(), new VideoReady(Guid.CreateVersion7(), "Movie", Guid.CreateVersion7(), 10, 2, DateTimeOffset.UtcNow)));
        Assert.True(await DeliverAsync(Guid.NewGuid(), new VideoReady(Guid.CreateVersion7(), "Episode", Guid.CreateVersion7(), 10, 2, DateTimeOffset.UtcNow)));
    }

    private async Task<bool> DeliverAsync<TEvent>(Guid messageId, TEvent integrationEvent)
        where TEvent : class
    {
        await using var scope = api.CreateScope();
        var handler = Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>());
        Assert.Equal("Catalog.Application.Videos.ApplyVideoEvents", handler.GetType().FullName);
        return await scope.ServiceProvider.GetRequiredService<IInbox>()
            .ProcessOnceAsync(messageId, handler.GetType().FullName!, token => handler.Handle(integrationEvent, token), Cancellation);
    }

    private static async Task<Guid> CreateMovieAsync(HttpClient admin)
    {
        var genre = (await (await admin.PostAsJsonAsync("/admin/genres", TestRequests.Genre($"g{Guid.NewGuid():N}"), Json, Cancellation)).Content.ReadFromJsonAsync<CreatedResponse>(Json, Cancellation))!.Id;
        var body = new { translations = TestRequests.Text("Video Testi", "Konu."), releaseYear = 2024, ageRating = "Age13", runtimeMinutes = 90, genreIds = new[] { genre }, credits = Array.Empty<object>(), version = 0 };
        var response = await admin.PostAsJsonAsync("/admin/movies", body, Json, Cancellation);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancellation));
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>(Json, Cancellation))!.Id;
    }

    private static async Task<(VideoState, bool)> StateAsync(HttpClient admin, Guid movie)
    {
        var details = (await (await admin.GetAsync($"/admin/movies/{movie}", Cancellation)).Content.ReadFromJsonAsync<MovieDetails>(Json, Cancellation))!;
        return (details.VideoStatus, details.HasPlayableVideo);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
