using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using NetBoxPlus.Testing;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Gateway.Tests.Sessions;

/// <summary>
/// One Valkey container for the session tests (the same image as <c>deploy/compose.yaml</c>).
/// Without Docker the tests are skipped.
/// </summary>
public sealed class ValkeyFixture : IAsyncLifetime
{
    private RedisContainer? _container;

    public bool IsAvailable { get; private set; }

    public string SkipReason { get; private set; } = string.Empty;

    public string ConnectionString => _container!.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new RedisBuilder("valkey/valkey:9.2-alpine").Build();
            await _container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (DockerAvailability.CanSkip(exception))
        {
            SkipReason = $"Docker is not available: {exception.Message}";
        }
    }

    public void SkipIfUnavailable() => Assert.SkipUnless(IsAvailable, SkipReason);

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition(nameof(ValkeyCollection))]
public sealed class ValkeyCollection : ICollectionFixture<ValkeyFixture>;

/// <summary>
/// A gateway wired for sessions: real Valkey, the fake auth service, a downstream service and
/// a clock the test moves. Several gateways can share the Valkey and the auth service, like
/// instances behind a load balancer.
/// </summary>
public sealed class SessionHarness : IAsyncDisposable
{
    private readonly List<GatewayFactory> _gateways = [];
    private readonly ValkeyFixture _valkey;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    private SessionHarness(ValkeyFixture valkey, FakeTimeProvider time, FakeAuthService auth, DownstreamService downstream, IReadOnlyDictionary<string, string?> settings)
    {
        _valkey = valkey;
        Time = time;
        Auth = auth;
        Downstream = downstream;
        _settings = settings;
        Gateway = AddGateway();
    }

    public FakeTimeProvider Time { get; }

    public FakeAuthService Auth { get; }

    public DownstreamService Downstream { get; }

    public GatewayFactory Gateway { get; }

    public static async Task<SessionHarness> StartAsync(ValkeyFixture valkey, IReadOnlyDictionary<string, string?>? settings = null)
    {
        valkey.SkipIfUnavailable();

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var auth = await FakeAuthService.StartAsync(time);
        var downstream = await DownstreamService.StartAsync();
        return new SessionHarness(valkey, time, auth, downstream, settings ?? new Dictionary<string, string?>());
    }

    /// <summary>Another gateway instance on the same Valkey and auth service.</summary>
    public GatewayFactory AddGateway()
    {
        var settings = new Dictionary<string, string?>(_settings)
        {
            ["ConnectionStrings:Valkey"] = _valkey.ConnectionString,
            ["Session:AuthAddress"] = Auth.Address,
        };

        var gateway = new GatewayFactory(Downstream.Address, settings, services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time)));
        _gateways.Add(gateway);
        return gateway;
    }

    /// <summary>A browser: keeps the session cookies it is given and sends them back.</summary>
    public Browser NewBrowser(GatewayFactory? gateway = null) => new((gateway ?? Gateway).CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));

    public async Task<IReadOnlyList<RedisKey>> ValkeyKeysAsync(string pattern)
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(_valkey.ConnectionString);
        return [.. connection.GetServers().Single().Keys(pattern: pattern)];
    }

    public async Task<IReadOnlyList<string>> ValkeyValuesAsync(string pattern)
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(_valkey.ConnectionString);
        var database = connection.GetDatabase();
        var values = new List<string>();
        foreach (var key in connection.GetServers().Single().Keys(pattern: pattern))
        {
            values.AddRange((await database.HashGetAllAsync(key)).Select(entry => entry.Value.ToString()));
        }

        return values;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var gateway in _gateways)
        {
            await gateway.DisposeAsync();
        }

        await Auth.DisposeAsync();
        await Downstream.DisposeAsync();
    }
}

/// <summary>An HTTP client that behaves like a browser towards the session cookie and CSRF token.</summary>
public sealed class Browser(HttpClient client)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public Dictionary<string, string> Cookies { get; } = new(StringComparer.Ordinal);

    public string? CsrfToken { get; set; }

    public HttpClient Client => client;

    public string? Origin { get; set; }

    public Task<HttpResponseMessage> SignInAsync(string email = "customer@example.com", string password = FakeAuthService.Password, string path = "/session/sign-in")
        => SendAsync(HttpMethod.Post, path, new { email, password });

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, bool withCsrf = true, string? bearer = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (Cookies.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", Cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        }

        if (withCsrf && CsrfToken is not null)
        {
            request.Headers.Add("X-CSRF-Token", CsrfToken);
        }

        if (Origin is not null)
        {
            request.Headers.Add("Origin", Origin);
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        var response = await client.SendAsync(request, Cancellation);
        Remember(response);

        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var text = await response.Content.ReadAsStringAsync(Cancellation);
            if (text.Length > 0 && JsonDocument.Parse(text).RootElement is { ValueKind: JsonValueKind.Object } root
                && root.TryGetProperty("csrfToken", out var csrf))
            {
                CsrfToken = csrf.GetString();
            }
        }

        return response;
    }

    private void Remember(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return;
        }

        foreach (var setCookie in setCookies)
        {
            var nameValue = setCookie.Split(';')[0];
            var separator = nameValue.IndexOf('=', StringComparison.Ordinal);
            var name = nameValue[..separator];
            var value = nameValue[(separator + 1)..];

            // A deletion sets an empty value with an expiry in the past.
            if (value.Length == 0)
            {
                Cookies.Remove(name);
            }
            else
            {
                Cookies[name] = value;
            }
        }
    }
}
