using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Options;

namespace Gateway.Tests;

public sealed class ServerClientTests : IAsyncLifetime
{
    private const string KeyHeader = "X-NetBoxPlus-Server-Key";
    private const string ForwardedFor = "X-Forwarded-For";

    // The test server has no remote address; this header gives a request one.
    private const string RemoteAddressHeader = "X-Test-Remote-Address";

    private static readonly Uri Public = new("/downstream/titles", UriKind.Relative);
    private static readonly Uri Admin = new("/admin/downstream/titles", UriKind.Relative);

    private readonly string _key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private DownstreamService _downstream = null!;
    private GatewayFactory _gateway = null!;

    public async ValueTask InitializeAsync()
    {
        _downstream = await DownstreamService.StartAsync();
        _gateway = Gateway(_key);
    }

    [Fact]
    public async Task VisitorsNamedByTheServer_EachHaveABudgetOfTheirOwn()
    {
        var client = _gateway.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, _key, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, _key, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(client, Public, _key, "203.0.113.1"));

        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, _key, "203.0.113.2"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, _key, "2001:db8::2"));
    }

    [Fact]
    public async Task WithoutTheKey_ForwardedForChangesNothing()
    {
        var client = _gateway.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, key: null, "203.0.113.1", remote: "198.51.100.7"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, key: null, "203.0.113.2", remote: "198.51.100.7"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(client, Public, key: null, "203.0.113.3", remote: "198.51.100.7"));
    }

    [Fact]
    public async Task AnIPv4Client_IsOneClient_OnEitherKindOfSocket()
    {
        var client = _gateway.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, key: null, forwardedFor: null, remote: "::ffff:203.0.113.9"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, key: null, forwardedFor: null, remote: "203.0.113.9"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(client, Public, key: null, forwardedFor: null, remote: "203.0.113.9"));
    }

    [Fact]
    public async Task AVisitorNamedByTheServer_IsCountedApartFromTheSameAddressCallingDirectly()
    {
        var client = _gateway.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, key: null, forwardedFor: null, remote: "203.0.113.9"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, key: null, forwardedFor: null, remote: "203.0.113.9"));

        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, _key, "203.0.113.9"));
    }

    [Fact]
    public async Task AVisitorOverTheirLimit_DoesNotUseUpTheServersTotal()
    {
        var client = _gateway.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            await SendAsync(client, Public, _key, "203.0.113.1");
        }

        // Two of the server's five went to that visitor; the eight refused ones took none.
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, _key, forwardedFor: null));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(client, Public, _key, forwardedFor: null));
    }

    [Fact]
    public async Task WithoutAVisitor_TheServerCountsOnlyAgainstItsTotal()
    {
        var client = _gateway.CreateClient();

        // No address, or one a proxy in between appended to, or not an address at all: no visitor,
        // and more requests than one visitor may make.
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, _key, forwardedFor: null));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, _key, "203.0.113.1, 10.0.0.5"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, _key, "web-server"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, _key, forwardedFor: null));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, _key, forwardedFor: null));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(client, Public, _key, forwardedFor: null));
    }

    [Fact]
    public async Task VisitorsTheServerNames_ShareItsTotal_SoMadeUpAddressesCannotMultiplyItsLoad()
    {
        var client = _gateway.CreateClient();

        for (var visitor = 1; visitor <= 5; visitor++)
        {
            Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, _key, $"203.0.113.{visitor}"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(client, Public, _key, "203.0.113.6"));

        // Clients calling the gateway themselves have nothing to do with the server's total.
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, key: null, forwardedFor: null, remote: "198.51.100.7"));
    }

    [Fact]
    public async Task AWrongKey_IsRefused_BeforeReachingTheService()
    {
        var client = _gateway.CreateClient();

        using var response = await client.SendAsync(Request(Public, _key + "x", "203.0.113.1"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("ServerClient.KeyInvalid", problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(_key, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

        var twice = Request(Public, _key, "203.0.113.1");
        twice.Headers.Add(KeyHeader, _key);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(twice, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Empty(_downstream.Received);
    }

    [Fact]
    public async Task AGatewayWithoutAKey_RefusesEveryKey()
    {
        await using var gateway = Gateway(key: null);
        var client = gateway.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(client, Public, _key, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, key: null, forwardedFor: null));
    }

    [Fact]
    public async Task TheKey_NeverReachesAService()
    {
        var client = _gateway.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Public, _key, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Admin, _key + "x", "203.0.113.1"));

        Assert.Equal(2, _downstream.Received.Count);
        Assert.All(_downstream.Received, request => Assert.False(request.Headers.ContainsKey(KeyHeader)));
    }

    [Fact]
    public async Task OtherGroups_IgnoreTheKey_AndCountTheConnection()
    {
        var client = _gateway.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, Admin, _key, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(client, Admin, _key, "203.0.113.2"));

        // The gateway's own session endpoint answers as it would without the key.
        using var session = await client.SendAsync(Request(new Uri("/session", UriKind.Relative), _key + "x", "203.0.113.1"), TestContext.Current.CancellationToken);
        using var problem = JsonDocument.Parse(await session.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Session.Required", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rejections_AreCountedByGroupAndKindOfClient()
    {
        using var rejections = new MetricCollector<long>(_gateway.Services.GetRequiredService<IMeterFactory>(), "NetBoxPlus.Gateway", "netboxplus.gateway.rate_limit.rejections");
        var client = _gateway.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            await SendAsync(client, Public, _key, "203.0.113.1");
            await SendAsync(client, Public, key: null, forwardedFor: null);
        }

        // Two of the server's five went to the visitor above (the refused one took none); the fourth of these is over the total.
        for (var i = 0; i < 4; i++)
        {
            await SendAsync(client, Public, _key, forwardedFor: null);
        }

        await SendAsync(client, Admin, key: null, forwardedFor: null);
        await SendAsync(client, Admin, key: null, forwardedFor: null);

        var counted = rejections.GetMeasurementSnapshot()
            .GroupBy(measurement => ((string?)measurement.Tags["group"], (string?)measurement.Tags["client"]))
            .ToDictionary(group => group.Key, group => group.Sum(measurement => measurement.Value));
        Assert.Equal(
            new Dictionary<(string?, string?), long>
            {
                [("public", "forwarded")] = 1,
                [("public", "direct")] = 1,
                [("public", "server")] = 1,
                [("admin", "direct")] = 1,
            },
            counted);
    }

    [Fact]
    public void AShortKey_StopsTheGateway()
    {
        using var gateway = Gateway(new string('k', 31));

        var failure = Assert.ThrowsAny<Exception>(() => gateway.CreateClient());
        Assert.Contains("ServerClient:Key", (failure as OptionsValidationException ?? failure.InnerException)?.Message ?? failure.Message, StringComparison.Ordinal);
    }

    private GatewayFactory Gateway(string? key)
        => new(
            _downstream.Address,
            new Dictionary<string, string?>
            {
                ["RateLimiting:Public:PermitLimit"] = "2",
                ["RateLimiting:Public:Window"] = "00:10:00",
                ["RateLimiting:Server:PermitLimit"] = "5",
                ["RateLimiting:Server:Window"] = "00:10:00",
                ["RateLimiting:Admin:PermitLimit"] = "1",
                ["RateLimiting:Admin:Window"] = "00:10:00",
                ["ServerClient:Key"] = key,
            },
            services => services.AddTransient<IStartupFilter, RemoteAddressFromHeader>());

    private static HttpRequestMessage Request(Uri path, string? key, string? forwardedFor, string? remote = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (key is not null)
        {
            request.Headers.Add(KeyHeader, key);
        }

        if (forwardedFor is not null)
        {
            request.Headers.Add(ForwardedFor, forwardedFor);
        }

        if (remote is not null)
        {
            request.Headers.Add(RemoteAddressHeader, remote);
        }

        return request;
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, Uri path, string? key, string? forwardedFor, string? remote = null)
    {
        using var response = await client.SendAsync(Request(path, key, forwardedFor, remote), TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    public async ValueTask DisposeAsync()
    {
        await _gateway.DisposeAsync();
        await _downstream.DisposeAsync();
    }

    private sealed class RemoteAddressFromHeader : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Headers.TryGetValue(RemoteAddressHeader, out var address))
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                    }

                    await nextMiddleware();
                });
                next(app);
            };
    }
}
