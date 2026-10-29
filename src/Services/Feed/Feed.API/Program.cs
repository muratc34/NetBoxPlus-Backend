using Feed.API.Items;
using Feed.Application;
using Feed.Infrastructure;
using Shared.Infrastructure.API.Hosting;
using Shared.Infrastructure.API.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddJwtAuthentication();
builder.Services.AddOpenApi();
builder.Services.AddFeedApplication();

if (!BuildTimeOpenApi.IsGeneratingDocument)
{
    builder.Services.AddFeedInfrastructure(builder.Configuration);
}

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapFeedEndpoints();
app.MapDefaultEndpoints();

app.Run();

/// <summary>Entry point; public so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
