using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Streaming.Infrastructure.Media;

namespace Streaming.IntegrationTests.Admin;

/// <summary>CORS rules belong to the whole storage account, which the services share locally.</summary>
[Collection(nameof(StreamingApiCollection))]
public sealed class StorageSetupTests(StreamingApiFixture api)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Setup_AddsItsRules_KeepsEveryoneElses_AndRunsAgainWithoutDuplicates()
    {
        Assert.SkipUnless(api.IsAvailable, "Docker is not available.");
        var blobs = new BlobServiceClient(api.VideosConnectionString);
        var properties = (await blobs.GetPropertiesAsync(Cancellation)).Value;
        var foreign = new BlobCorsRule { AllowedOrigins = "https://other.test", AllowedMethods = "PUT", AllowedHeaders = "x-ms-blob-type", ExposedHeaders = string.Empty, MaxAgeInSeconds = 60 };
        properties.Cors = [foreign];
        await blobs.SetPropertiesAsync(properties, Cancellation);

        await VideoStorageSetup.EnsureAsync(api.VideosConnectionString, ["https://admin.test"], ["https://app.test"], Cancellation);
        await VideoStorageSetup.EnsureAsync(api.VideosConnectionString, ["https://admin.test"], ["https://app.test"], Cancellation);

        var rules = (await blobs.GetPropertiesAsync(Cancellation)).Value.Cors;
        Assert.Equal(3, rules.Count);
        Assert.Contains(rules, rule => rule.AllowedOrigins == "https://other.test");
        Assert.Contains(rules, rule => rule.AllowedOrigins == "https://admin.test" && rule.AllowedMethods == "PUT");
        Assert.Contains(rules, rule => rule.AllowedOrigins == "https://app.test" && rule.AllowedMethods == "GET,HEAD");
    }
}
