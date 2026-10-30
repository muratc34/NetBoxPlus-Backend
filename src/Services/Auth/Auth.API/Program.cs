using Auth.API.Security;
using Auth.API.WellKnown;
using Auth.Application;
using Auth.Infrastructure;
using Shared.Infrastructure.API.Hosting;
using Shared.Infrastructure.API.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddJwtAuthentication();
builder.Services.AddOpenApi();
builder.Services.AddAuthApplication();

if (!BuildTimeOpenApi.IsGeneratingDocument)
{
    builder.Services.AddAuthInfrastructure(builder.Configuration);
    builder.Services.AddSelfIssuedTokenValidation();
}

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapWellKnownEndpoints();

// Sign-in, token and admin endpoints come in the next steps of phase 1.
app.MapDefaultEndpoints();

app.Run();

/// <summary>Entry point; public so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
