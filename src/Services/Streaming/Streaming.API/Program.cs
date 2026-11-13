using System.Text.Json.Serialization;
using Shared.Infrastructure.API.Hosting;
using Streaming.API.Admin;
using Streaming.API.Playback;
using Shared.Infrastructure.API.Security;
using Streaming.Application;
using Streaming.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddJwtAuthentication();
builder.Services.AddOpenApi();

// Enums as names in JSON: the generated TypeScript client and the admin panel read them directly.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddStreamingApplication();

if (!BuildTimeOpenApi.IsGeneratingDocument)
{
    builder.Services.AddStreamingInfrastructure(builder.Configuration);
}

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapVideoAdminEndpoints();
app.MapPlaybackEndpoints();
app.MapDefaultEndpoints();

app.Run();

/// <summary>Entry point; public so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
