using Gateway.Hosting;
using Shared.Infrastructure.API.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddGateway();

var app = builder.Build();

app.UseServiceDefaults();
app.UseCors();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGateway();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
