using System.Text.Json.Serialization;
using Catalog.API.Admin;
using Catalog.API.Public;
using Catalog.Application;
using Catalog.Infrastructure;
using Shared.Infrastructure.API.Hosting;
using Shared.Infrastructure.API.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddJwtAuthentication();
builder.Services.AddOpenApi();

// Enums as names in JSON: the generated TypeScript client and the admin panel read them directly.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCatalogApplication();

if (!BuildTimeOpenApi.IsGeneratingDocument)
{
    builder.Services.AddCatalogInfrastructure(builder.Configuration);
}

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapMovieAdminEndpoints();
app.MapSeriesAdminEndpoints();
app.MapPersonAdminEndpoints();
app.MapGenreAdminEndpoints();
app.MapBrowseEndpoints();
app.MapDefaultEndpoints();

app.Run();

/// <summary>Entry point; public so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
