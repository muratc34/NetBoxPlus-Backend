using System.Net;
using Azure.Storage.Blobs;
using Catalog.Infrastructure;
using Catalog.Infrastructure.Images;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Catalog.IntegrationTests.Admin;

/// <summary>
/// Inside containers the storage endpoint in the connection string is one browsers cannot reach;
/// the upload and image addresses handed out must use the public one and still work.
/// </summary>
[Collection(nameof(CatalogApiCollection))]
public sealed class PublicBlobAddressTests(CatalogApiFixture api)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UploadAndImageAddresses_UseThePublicEndpoint_AndStillWork()
    {
        Assert.SkipUnless(api.IsAvailable, "Docker is not available.");
        var blobs = new BlobServiceClient(api.ImagesConnectionString);
        var publicEndpoint = OtherHostFor(blobs.Uri);
        var storage = Storage(blobs, publicEndpoint);
        var fileId = Guid.CreateVersion7();

        var upload = storage.CreateUploadAddress(fileId, DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.StartsWith(publicEndpoint.AbsoluteUri.TrimEnd('/') + "/" + BlobImageStorage.IncomingContainer + "/", upload.AbsoluteUri, StringComparison.Ordinal);

        using var http = new HttpClient();
        using var put = new HttpRequestMessage(HttpMethod.Put, upload) { Content = new ByteArrayContent([1, 2, 3]) };
        put.Headers.Add("x-ms-blob-type", "BlockBlob");
        Assert.Equal(HttpStatusCode.Created, (await http.SendAsync(put, Cancellation)).StatusCode);
        Assert.Equal(3, await storage.GetUploadSizeAsync(fileId, Cancellation));

        var name = $"{Guid.CreateVersion7():N}.png";
        await storage.PublishAsync(fileId, name, "image/png", [9, 8, 7], Cancellation);
        var image = storage.PublicAddress(name);
        Assert.Equal(publicEndpoint.Authority, image.Authority);
        Assert.Equal([9, 8, 7], await http.GetByteArrayAsync(image, Cancellation));
    }

    [Fact]
    public void WithoutAPublicEndpoint_AddressesAreHandedOutAsMade()
    {
        Assert.SkipUnless(api.IsAvailable, "Docker is not available.");
        var blobs = new BlobServiceClient(api.ImagesConnectionString);

        var image = Storage(blobs, publicEndpoint: null).PublicAddress("poster.png");

        Assert.Equal(blobs.Uri.AbsoluteUri.TrimEnd('/') + "/" + BlobImageStorage.ImagesContainer + "/poster.png", image.AbsoluteUri);
    }

    [Fact]
    public void AnAddressOutsideTheStorageEndpoint_IsNeverRewritten()
    {
        var blobs = new BlobServiceClient(new Uri("http://storage.internal:10000/account"));
        var addresses = new PublicBlobAddresses(blobs, Options.Create(new BlobStorageOptions { PublicEndpoint = new Uri("https://cdn.example.test/") }));

        Assert.Equal(new Uri("https://cdn.example.test/images/a.png"), addresses.ToPublic(new Uri("http://storage.internal:10000/account/images/a.png")));
        Assert.Throws<InvalidOperationException>(() => addresses.ToPublic(new Uri("http://elsewhere.test/account/images/a.png")));
        Assert.Throws<InvalidOperationException>(() => addresses.ToPublic(new Uri("http://storage.internal:10000/account2/images/a.png")));
    }

    [Theory]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1", true)]
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
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BlobStorage:PublicEndpoint"] = "ftp://127.0.0.1/devstoreaccount1" })
            .Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        services.AddCatalogImages("UseDevelopmentStorage=true");
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value);
    }

    private static BlobImageStorage Storage(BlobServiceClient blobs, Uri? publicEndpoint)
        => new(blobs, new PublicBlobAddresses(blobs, Options.Create(new BlobStorageOptions { PublicEndpoint = publicEndpoint })));

    private static Uri OtherHostFor(Uri endpoint)
    {
        var host = endpoint.Host == "localhost" ? "127.0.0.1" : "localhost";
        return new UriBuilder(endpoint) { Host = host }.Uri;
    }
}
