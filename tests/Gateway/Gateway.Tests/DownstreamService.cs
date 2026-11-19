using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Tests;

/// <summary>
/// A service behind the gateway, on a real Kestrel port (the gateway forwards over the network).
/// It answers every request with 200 (201 for POST) and records what it received.
/// </summary>
public sealed class DownstreamService : IAsyncDisposable
{
    private readonly WebApplication _app;

    private DownstreamService(WebApplication app) => _app = app;

    public string Address { get; private set; } = string.Empty;

    public ConcurrentQueue<ReceivedRequest> Received { get; } = new();

    public static async Task<DownstreamService> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        var service = new DownstreamService(app);

        app.Run(context =>
        {
            service.Received.Enqueue(new ReceivedRequest(
                context.Request.Method,
                context.Request.Path + context.Request.QueryString,
                context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase)));

            // POST creates "{path}/1", with the service's own path in Location.
            return HttpMethods.IsPost(context.Request.Method)
                ? Results.Created($"{context.Request.Path}/1", new { received = true }).ExecuteAsync(context)
                : Results.Ok(new { received = true }).ExecuteAsync(context);
        });

        await app.StartAsync();
        service.Address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();

        return service;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

public sealed record ReceivedRequest(string Method, string PathAndQuery, IReadOnlyDictionary<string, string> Headers);
