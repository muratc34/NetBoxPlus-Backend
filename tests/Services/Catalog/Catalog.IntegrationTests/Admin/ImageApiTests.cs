using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.API.Admin;
using Catalog.Application.Browse.GetPublishedMovie;
using Catalog.Application.Images.RequestImageUpload;
using Catalog.Application.Movies.GetMovie;
using Catalog.Application.People.GetPeople;
using Catalog.Domain.Images;
using Catalog.Infrastructure.Images;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.IntegrationTests.Admin;

[Collection(nameof(CatalogApiCollection))]
public sealed class ImageApiTests(CatalogApiFixture api)
{
    private static readonly string[] Everything =
    [
        "catalog:movies:read", "catalog:movies:write", "catalog:movies:publish", "catalog:movies:delete",
        "catalog:people:read", "catalog:people:write", "catalog:genres:read", "catalog:genres:write", "catalog:images:upload",
    ];

    private static readonly JsonSerializerOptions Json = CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UploadedPoster_IsChecked_Stored_AndReadableByAnyone()
    {
        using var client = api.CreateClient(Everything);
        using var visitor = api.CreateAnonymousClient();
        var movie = await PublishedMovieAsync(client);

        var details = await ReadAsync<PublishedMovie>(await visitor.GetAsync($"/movies/{movie}", Cancellation));

        using var plain = new HttpClient();
        var image = await plain.GetAsync(details.PosterUrl, Cancellation);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".png", details.PosterUrl!.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(TestImages.Poster(), await image.Content.ReadAsByteArrayAsync(Cancellation));
    }

    [Fact]
    public async Task UploadAddress_CannotReadAnything()
    {
        using var client = api.CreateClient(Everything);
        var ticket = await TicketAsync(client);
        await TestImages.PutAsync(ticket.UploadAddress, TestImages.Poster(), Cancellation);

        using var plain = new HttpClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await plain.GetAsync(ticket.UploadAddress, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task TheFileIsJudgedByItsContent_NotByWhatItClaims()
    {
        using var client = api.CreateClient(Everything);
        var movie = await MovieAsync(client);
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");
        var text = Encoding.UTF8.GetBytes("this is not a picture, whatever its name says");

        Assert.Equal("Image.UnsupportedType", await AttachCodeAsync(client, movie, "poster", svg));
        Assert.Equal("Image.UnsupportedType", await AttachCodeAsync(client, movie, "poster", text));
    }

    [Fact]
    public async Task AFileWithABrokenHeader_IsRefused_NotAServerError()
    {
        using var client = api.CreateClient(Everything);
        var movie = await MovieAsync(client);
        byte[] broken = [.. TestImages.Png(300, 450)[..12], 0x01, 0x02, 0x03, 0x04, 0x05];

        Assert.Equal("Image.UnsupportedType", await AttachCodeAsync(client, movie, "poster", broken));
    }

    [Fact]
    public async Task AGifIsRefused_EvenThoughItIsAnImage()
    {
        using var client = api.CreateClient(Everything);
        var movie = await MovieAsync(client);

        Assert.Equal("Image.UnsupportedType", await AttachCodeAsync(client, movie, "poster", TestImages.Gif(300, 450)));
    }

    [Fact]
    public async Task ImagesOfTheWrongSizeOrShape_AreRefused()
    {
        using var client = api.CreateClient(Everything);
        var movie = await MovieAsync(client);

        Assert.Equal("Image.TooSmall", await AttachCodeAsync(client, movie, "poster", TestImages.Png(100, 150)));
        Assert.Equal("Image.WrongAspectRatio", await AttachCodeAsync(client, movie, "poster", TestImages.Png(450, 450)));
        Assert.Equal("Image.WrongAspectRatio", await AttachCodeAsync(client, movie, "backdrop", TestImages.Png(1500, 1500)));
    }

    [Fact]
    public async Task AFileOverTheLimit_IsRefusedBeforeBeingRead()
    {
        using var client = api.CreateClient(Everything);
        var movie = await MovieAsync(client);

        Assert.Equal("Image.TooLarge", await AttachCodeAsync(client, movie, "poster", new byte[ImageRules.MaxBytes + 1]));
    }

    [Fact]
    public async Task AJpeg_IsReadAndStoredAsAJpeg()
    {
        using var client = api.CreateClient(Everything);
        var movie = await MovieAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, (await AttachAsync(client, movie, "poster", await TestImages.UploadAsync(client, TestImages.Jpeg(300, 450), Json, Cancellation))).StatusCode);

        var address = (await GetAsync(client, movie)).PosterUrl!;
        Assert.EndsWith(".jpg", address.AbsolutePath, StringComparison.Ordinal);
        using var plain = new HttpClient();
        Assert.Equal("image/jpeg", (await plain.GetAsync(address, Cancellation)).Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AWebp_IsReadAndStoredAsAWebp()
    {
        using var client = api.CreateClient(Everything);
        var movie = await MovieAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, (await AttachAsync(client, movie, "poster", await TestImages.UploadAsync(client, TestImages.Webp(300, 450), Json, Cancellation))).StatusCode);

        Assert.EndsWith(".webp", (await GetAsync(client, movie)).PosterUrl!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUploadWorksOnce()
    {
        using var client = api.CreateClient(Everything);
        var movie = await MovieAsync(client);
        var upload = await TestImages.UploadAsync(client, TestImages.Poster(), Json, Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, (await AttachAsync(client, movie, "poster", upload)).StatusCode);

        Assert.Equal("Image.UploadAlreadyUsed", await CodeAsync(await AttachAsync(client, movie, "poster", upload)));
    }

    [Fact]
    public async Task UnknownUnfinishedAndExpiredUploads_AreRefused()
    {
        using var client = api.CreateClient(Everything);
        var movie = await MovieAsync(client);
        var unfinished = (await TicketAsync(client)).UploadId;
        var expired = await TestImages.UploadAsync(client, TestImages.Poster(), Json, Cancellation);
        await ExpireAsync(expired);

        Assert.Equal("Image.UploadNotFound", await CodeAsync(await AttachAsync(client, movie, "poster", Guid.CreateVersion7())));
        Assert.Equal("Image.NotUploaded", await CodeAsync(await AttachAsync(client, movie, "poster", unfinished)));
        Assert.Equal("Image.UploadExpired", await CodeAsync(await AttachAsync(client, movie, "poster", expired)));
    }

    [Fact]
    public async Task StaleVersion_DoesNotConsumeTheUpload()
    {
        using var client = api.CreateClient(Everything);
        var movie = await MovieAsync(client);
        var upload = await TestImages.UploadAsync(client, TestImages.Poster(), Json, Cancellation);
        var version = (await GetAsync(client, movie)).Version;

        var stale = await client.PutAsJsonAsync($"/admin/movies/{movie}/images/poster", new AttachImageRequest(upload, version + 1), Json, Cancellation);

        Assert.Equal("Content.VersionConflict", await CodeAsync(stale));
        Assert.Equal(HttpStatusCode.NoContent, (await AttachAsync(client, movie, "poster", upload)).StatusCode);
    }

    [Fact]
    public async Task ReplacingAnImage_RetiresTheOldOne()
    {
        using var client = api.CreateClient(Everything);
        var movie = await MovieAsync(client);
        await AttachAsync(client, movie, "poster", await TestImages.UploadAsync(client, TestImages.Poster(), Json, Cancellation));
        var first = (await GetAsync(client, movie)).PosterUrl!;

        await AttachAsync(client, movie, "poster", await TestImages.UploadAsync(client, TestImages.Png(600, 900), Json, Cancellation));

        var second = (await GetAsync(client, movie)).PosterUrl!;
        Assert.NotEqual(first, second);
        var old = await FileAsync(Path.GetFileName(first.AbsolutePath));
        Assert.Equal(ImageFileStatus.Retired, old.Status);
        Assert.NotNull(old.RetiredAt);

        // Still there for pages that are already open.
        using var plain = new HttpClient();
        Assert.Equal(HttpStatusCode.OK, (await plain.GetAsync(first, Cancellation)).StatusCode);
    }

    [Fact]
    public async Task PublishedMovie_CannotLoseItsPoster()
    {
        using var client = api.CreateClient(Everything);
        var movie = await PublishedMovieAsync(client);
        var version = (await GetAsync(client, movie)).Version;

        Assert.Equal("Content.PosterRequired", await CodeAsync(await client.DeleteAsync($"/admin/movies/{movie}/images/poster?version={version}", Cancellation)));
    }

    [Fact]
    public async Task Backdrop_CanBeAttachedAndRemoved()
    {
        using var client = api.CreateClient(Everything);
        var movie = await MovieAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, (await AttachAsync(client, movie, "backdrop", await TestImages.UploadAsync(client, TestImages.Png(1280, 720), Json, Cancellation))).StatusCode);
        var attached = await GetAsync(client, movie);
        Assert.NotNull(attached.BackdropUrl);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/admin/movies/{movie}/images/backdrop?version={attached.Version}", Cancellation)).StatusCode);
        Assert.Null((await GetAsync(client, movie)).BackdropUrl);
    }

    [Fact]
    public async Task Person_GetsAndLosesAPhoto()
    {
        using var client = api.CreateClient(Everything);
        var person = await PostAsync(client, "/admin/people", TestRequests.Person("Ada", null));
        var upload = await TestImages.UploadAsync(client, TestImages.Png(200, 300), Json, Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/admin/people/{person}/photo", new AttachPhotoRequest(upload), Json, Cancellation)).StatusCode);
        Assert.NotNull((await ReadAsync<PersonResponse>(await client.GetAsync($"/admin/people/{person}", Cancellation))).PhotoUrl);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/admin/people/{person}/photo", Cancellation)).StatusCode);
        Assert.Null((await ReadAsync<PersonResponse>(await client.GetAsync($"/admin/people/{person}", Cancellation))).PhotoUrl);
    }

    [Fact]
    public async Task CleanUp_RemovesWhatNothingNeeds_AndLeavesTheRest()
    {
        using var client = api.CreateClient(Everything);
        using var plain = new HttpClient();
        var movie = await MovieAsync(client);

        var abandoned = await TestImages.UploadAsync(client, TestImages.Poster(), Json, Cancellation);
        var open = await TestImages.UploadAsync(client, TestImages.Poster(), Json, Cancellation);
        await ExpireAsync(abandoned);

        await AttachAsync(client, movie, "poster", await TestImages.UploadAsync(client, TestImages.Poster(), Json, Cancellation));
        var oldAddress = (await GetAsync(client, movie)).PosterUrl!;
        await AttachAsync(client, movie, "poster", await TestImages.UploadAsync(client, TestImages.Poster(), Json, Cancellation));
        var activeAddress = (await GetAsync(client, movie)).PosterUrl!;
        await AttachAsync(client, movie, "backdrop", await TestImages.UploadAsync(client, TestImages.Png(1280, 720), Json, Cancellation));
        var recentAddress = (await GetAsync(client, movie)).BackdropUrl!;
        await AttachAsync(client, movie, "backdrop", await TestImages.UploadAsync(client, TestImages.Png(1280, 720), Json, Cancellation));
        await RetireLongAgoAsync(Path.GetFileName(oldAddress.AbsolutePath));

        await using (var scope = api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ImageCleaner>().CleanAsync(Cancellation);
        }

        await using (var scope = api.CreateScope())
        {
            var files = scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Set<ImageFile>();
            Assert.False(await files.AnyAsync(file => file.Id == abandoned, Cancellation));
            Assert.True(await files.AnyAsync(file => file.Id == open, Cancellation));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await plain.GetAsync(oldAddress, Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await plain.GetAsync(activeAddress, Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await plain.GetAsync(recentAddress, Cancellation)).StatusCode);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static async Task<Guid> PostAsync(HttpClient client, string path, object body)
        => (await ReadAsync<CreatedResponse>(await client.PostAsJsonAsync(path, body, Json, Cancellation))).Id;

    private static async Task<Guid> MovieAsync(HttpClient client, params Guid[] genres)
        => await PostAsync(client, "/admin/movies", new { translations = TestRequests.Text("Night Train", "A journey."), releaseYear = 2024, ageRating = "Age13", runtimeMinutes = 120, genreIds = genres });

    private static async Task<Guid> PublishedMovieAsync(HttpClient client)
    {
        var genre = await PostAsync(client, "/admin/genres", TestRequests.Genre($"Genre {Guid.NewGuid():N}"));
        var movie = await MovieAsync(client, genre);
        await AttachAsync(client, movie, "poster", await TestImages.UploadAsync(client, TestImages.Poster(), Json, Cancellation));
        Assert.True((await client.PostAsync($"/admin/movies/{movie}/publish", null, Cancellation)).IsSuccessStatusCode);
        return movie;
    }

    private static async Task<ImageUploadTicket> TicketAsync(HttpClient client)
        => await ReadAsync<ImageUploadTicket>(await client.PostAsync("/admin/images/uploads", null, Cancellation));

    private static async Task<MovieDetails> GetAsync(HttpClient client, Guid id)
        => await ReadAsync<MovieDetails>(await client.GetAsync($"/admin/movies/{id}", Cancellation));

    private static async Task<HttpResponseMessage> AttachAsync(HttpClient client, Guid movie, string slot, Guid upload)
        => await client.PutAsJsonAsync($"/admin/movies/{movie}/images/{slot}", new AttachImageRequest(upload, (await GetAsync(client, movie)).Version), Json, Cancellation);

    private static async Task<string?> AttachCodeAsync(HttpClient client, Guid movie, string slot, byte[] content)
        => await CodeAsync(await AttachAsync(client, movie, slot, await TestImages.UploadAsync(client, content, Json, Cancellation)));

    private async Task ExpireAsync(Guid fileId)
    {
        await using var scope = api.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Set<ImageFile>()
            .Where(file => file.Id == fileId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(file => file.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Cancellation);
    }

    private async Task RetireLongAgoAsync(string name)
    {
        await using var scope = api.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Set<ImageFile>()
            .Where(file => file.Name == name)
            .ExecuteUpdateAsync(setters => setters.SetProperty(file => file.RetiredAt, DateTimeOffset.UtcNow.AddDays(-2)), Cancellation);
    }

    private async Task<ImageFile> FileAsync(string name)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Set<ImageFile>().AsNoTracking().SingleAsync(file => file.Name == name, Cancellation);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancellation)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Cancellation))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("code").GetString();
}
