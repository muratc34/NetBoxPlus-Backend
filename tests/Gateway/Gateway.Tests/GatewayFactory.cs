using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Tests;

/// <summary>
/// The gateway in memory with the routes of <c>appsettings.json</c> plus a test-only <c>sample</c> service
/// at <paramref name="sampleAddress"/>, and any further settings on top.
/// </summary>
public sealed class GatewayFactory(
    string sampleAddress,
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    public const string PublicOrigin = "https://app.test";
    public const string AdminOrigin = "https://admin.test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": that would point the cluster at a locally running service.
        builder.UseEnvironment("Testing");
        // The product has no public routes of its own yet, so the tests bring a service of their own.
        builder.UseSetting("ReverseProxy:Routes:sample-public:ClusterId", "sample");
        builder.UseSetting("ReverseProxy:Routes:sample-public:Match:Path", "/sample/{**remainder}");
        builder.UseSetting("ReverseProxy:Routes:sample-public:Metadata:RouteGroup", "public");
        builder.UseSetting("ReverseProxy:Routes:sample-public:Transforms:0:PathRemovePrefix", "/sample");
        builder.UseSetting("ReverseProxy:Routes:sample-admin:ClusterId", "sample");
        builder.UseSetting("ReverseProxy:Routes:sample-admin:Match:Path", "/admin/sample/{**remainder}");
        builder.UseSetting("ReverseProxy:Routes:sample-admin:Metadata:RouteGroup", "admin");
        builder.UseSetting("ReverseProxy:Routes:sample-admin:Transforms:0:PathPattern", "/admin/{**remainder}");
        builder.UseSetting("ReverseProxy:Clusters:sample:Destinations:primary:Address", sampleAddress);
        builder.UseSetting("Cors:PublicOrigins:0", PublicOrigin);
        builder.UseSetting("Cors:AdminOrigins:0", AdminOrigin);

        // Nothing listens there: tests that need sessions pass a real Valkey and auth service.
        builder.UseSetting("ConnectionStrings:Valkey", "127.0.0.1:1,connectTimeout=200");
        builder.UseSetting("Session:AuthAddress", "http://127.0.0.1:1");

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }
}
