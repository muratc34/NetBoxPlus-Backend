using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Streaming.Application;
using Streaming.Infrastructure;

// Takes queued videos one at a time and converts them with ffmpeg. Run as many as the machine
// allows: each video is taken by exactly one of them.
// Its settings sit next to the program, wherever it is started from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Configuration.AddUserSecrets(typeof(StreamingInfrastructureServiceCollectionExtensions).Assembly, optional: true);
builder.Configuration.AddUserSecrets<TranscoderMarker>(optional: true);

builder.Services.AddStreamingApplication();
builder.Services.AddStreamingTranscoder(builder.Configuration);

builder.Build().Run();

/// <summary>Anchors the user secrets lookup to this assembly.</summary>
internal sealed class TranscoderMarker;
