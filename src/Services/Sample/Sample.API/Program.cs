using Sample.API.Notes;
using Sample.Application;
using Sample.Infrastructure;
using Shared.Infrastructure.API.Hosting;
using Shared.Infrastructure.API.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddJwtAuthentication();
builder.Services.AddOpenApi();
builder.Services.AddSampleApplication();

if (!BuildTimeOpenApi.IsGeneratingDocument)
{
    builder.Services.AddSampleInfrastructure(builder.Configuration);
}

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapNoteEndpoints();
app.MapDefaultEndpoints();

app.Run();

/// <summary>Entry point; public so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
