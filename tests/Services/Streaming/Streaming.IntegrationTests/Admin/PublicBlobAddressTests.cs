using System.Net;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Streaming.Infrastructure;
using Streaming.Infrastructure.Media;

namespace Streaming.IntegrationTests.Admin;

/// <summary>
/// Inside containers the storage endpoint in the connection string is one browsers cannot reach;
/// the addresses handed out must use the public one and still work.
/// </summary>
[Collection(nameof(StreamingApiCollection))]
public sealed class PublicBlobAddressTests(StreamingApiFixture api)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SignedAddresses_UseThePublicEndpoint_AndStillWork()
    {
        Assert.SkipUnless(api.IsAvailable, "Docker is not available.");
        var blobs = new BlobServiceClient(api.VideosConnectionString);
        var publicEndpoint = OtherHostFor(blobs.Uri);
        var storage = Storage(blobs, publicEndpoint);
        var videoId = Guid.CreateVersion7();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var upload = storage.CreateUploadAddress(videoId, expiresAt);
        Assert.StartsWith(publicEndpoint.AbsoluteUri.TrimEnd('/') + "/" + VideoBlobStorage.UploadsContainer + "/", upload.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("sig=", upload.Query, StringComparison.Ordinal);

        using var http = new HttpClient();
        using var put = new HttpRequestMessage(HttpMethod.Put, upload) { Content = new ByteArrayContent([1, 2, 3]) };
        put.Headers.Add("x-ms-blob-type", "BlockBlob");
        Assert.Equal(HttpStatusCode.Created, (await http.SendAsync(put, Cancellation)).StatusCode);

        await blobs.GetBlobContainerClient(VideoBlobStorage.VideosContainer)
            .GetBlobClient($"{VideoBlobStorage.FolderOf(videoId)}/720p/index.m3u8")
            .UploadAsync(BinaryData.FromString("#EXTM3U"), Cancellation);
        var read = storage.CreateReadAddress(videoId, "720p/index.m3u8", expiresAt);
        Assert.Equal(publicEndpoint.Authority, read.Authority);
        Assert.Equal("#EXTM3U", await http.GetStringAsync(read, Cancellation));
    }

    [Fact]
    public void WithoutAPublicEndpoint_AddressesAreHandedOutAsMade()
    {
        Assert.SkipUnless(api.IsAvailable, "Docker is not available.");
        var blobs = new BlobServiceClient(api.VideosConnectionString);

        var address = Storage(blobs, publicEndpoint: null).CreateReadAddress(Guid.CreateVersion7(), "master.m3u8", DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.StartsWith(blobs.Uri.AbsoluteUri.TrimEnd('/') + "/" + VideoBlobStorage.VideosContainer + "/", address.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAddressOutsideTheStorageEndpoint_IsNeverRewritten()
    {
        var blobs = new BlobServiceClient(new Uri("http://storage.internal:10000/account"));
        var addresses = new PublicBlobAddresses(blobs, Options.Create(new BlobStorageOptions { PublicEndpoint = new Uri("http://127.0.0.1:10000/account") }));

        Assert.Equal(new Uri("http://127.0.0.1:10000/account/videos/a.ts?sig=x"), addresses.ToPublic(new Uri("http://storage.internal:10000/account/videos/a.ts?sig=x")));
        Assert.Throws<InvalidOperationException>(() => addresses.ToPublic(new Uri("http://elsewhere.test/account/videos/a.ts")));
        Assert.Throws<InvalidOperationException>(() => addresses.ToPublic(new Uri("http://storage.internal:10000/account2/videos/a.ts")));
    }

    [Theory]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1", true)]
    [InlineData("https://cdn.example.test", true)]
    [InlineData("ftp://127.0.0.1/devstoreaccount1", false)]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1?sv=1", false)]
    public void ThePublicEndpoint_MustBeAPlainHttpAddress(string endpoint, bool valid)
    {
        Assert.Equal(valid, new BlobStorageOptions { PublicEndpoint = new Uri(endpoint) }.IsValid());
        Assert.False(new BlobStorageOptions { PublicEndpoint = new Uri("/relative", UriKind.Relative) }.IsValid());
        Assert.True(new BlobStorageOptions().IsValid());
    }

    [Fact]
    public void AnInvalidPublicEndpoint_StopsTheServiceFromStarting()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=localhost;Database=streaming",
                ["ConnectionStrings:Videos"] = "UseDevelopmentStorage=true",
                ["BlobStorage:PublicEndpoint"] = "http://127.0.0.1:10000/devstoreaccount1?sv=1",
            })
            .Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        services.AddStreamingInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value);
    }

    private static VideoBlobStorage Storage(BlobServiceClient blobs, Uri? publicEndpoint)
        => new(blobs, new PublicBlobAddresses(blobs, Options.Create(new BlobStorageOptions { PublicEndpoint = publicEndpoint })));

    /// <summary>The same storage under another name, as a browser outside the container network would see it.</summary>
    private static Uri OtherHostFor(Uri endpoint)
    {
        var host = endpoint.Host == "localhost" ? "127.0.0.1" : "localhost";
        return new UriBuilder(endpoint) { Host = host }.Uri;
    }
}
