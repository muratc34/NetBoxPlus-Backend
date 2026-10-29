using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gateway.Tests;

/// <summary>
/// The gateway in memory with the routes of <c>appsettings.json</c>, its <c>sample</c> cluster
/// pointed at <paramref name="sampleAddress"/>, and any further settings on top.
/// </summary>
public sealed class GatewayFactory(string sampleAddress, IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    public const string PublicOrigin = "https://app.test";
    public const string AdminOrigin = "https://admin.test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": that would point the cluster at a locally running service.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ReverseProxy:Clusters:sample:Destinations:primary:Address", sampleAddress);
        builder.UseSetting("Cors:PublicOrigins:0", PublicOrigin);
        builder.UseSetting("Cors:AdminOrigins:0", AdminOrigin);

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }
    }
}
