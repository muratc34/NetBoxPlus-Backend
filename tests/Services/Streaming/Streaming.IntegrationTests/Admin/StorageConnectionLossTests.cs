using System.Diagnostics;
using System.Text.RegularExpressions;
using Azure.Storage.Sas;
using NetBoxPlus.Testing;

namespace Streaming.IntegrationTests.Admin;

/// <summary>Requests to storage survive a connection that the port forwarding swallows.</summary>
[Collection(nameof(StreamingApiCollection))]
public sealed partial class StorageConnectionLossTests(StreamingApiFixture api)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheBlobClient_GivesUpASwallowedRequestEarly_AndTriesAgain()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        await using var proxy = Proxy(out var throughProxy);
        var container = StorageConnections.CreateClient(throughProxy).GetBlobContainerClient($"loss{Guid.NewGuid():N}");

        var watch = Stopwatch.StartNew();
        await container.CreateAsync(cancellationToken: Cancellation);
        watch.Stop();

        Assert.True((await StorageConnections.CreateClient(api.VideosConnectionString).GetBlobContainerClient(container.Name).ExistsAsync(Cancellation)).Value);
        Assert.Equal(1, proxy.Swallowed);

        // Without the short timeout the SDK waits 100 s for the lost request.
        Assert.InRange(watch.Elapsed, StorageConnections.RequestTimeout - TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task TheHttpClient_GivesUpASwallowedRequestEarly_AndTriesAgain()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        var container = StorageConnections.CreateClient(api.VideosConnectionString).GetBlobContainerClient($"loss{Guid.NewGuid():N}");
        await container.CreateAsync(cancellationToken: Cancellation);
        var blob = container.GetBlobClient("file.txt");
        await blob.UploadAsync(BinaryData.FromString("kept"), Cancellation);
        var direct = blob.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(5));
        await using var proxy = Proxy(out _);
        var throughProxy = new UriBuilder(direct) { Port = proxy.Port }.Uri;
        using var http = StorageConnections.CreateHttpClient();

        var watch = Stopwatch.StartNew();
        var body = await http.GetStringAsync(throughProxy, Cancellation);
        watch.Stop();

        Assert.Equal(("kept", 1), (body, proxy.Swallowed));
        Assert.InRange(watch.Elapsed, StorageConnections.RequestTimeout - TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
    }

    /// <summary>A proxy in front of the storage container that swallows the first connection.</summary>
    private BlackHoleProxy Proxy(out string throughProxy)
    {
        var endpoint = BlobEndpoint().Match(api.VideosConnectionString);
        var address = new Uri(endpoint.Groups["address"].Value);
        var proxy = new BlackHoleProxy(address.Host, address.Port, swallowed: 1);
        throughProxy = api.VideosConnectionString.Replace(
            endpoint.Groups["address"].Value,
            new UriBuilder(address) { Port = proxy.Port }.Uri.ToString().TrimEnd('/'),
            StringComparison.Ordinal);
        return proxy;
    }

    [GeneratedRegex("BlobEndpoint=(?<address>[^;]+)")]
    private static partial Regex BlobEndpoint();
}
