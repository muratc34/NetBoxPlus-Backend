using Gateway.Hosting;
using Shared.Infrastructure.API.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddGateway();

var app = builder.Build();

app.UseServiceDefaults();
app.UseCors();
app.UseRateLimiter();

app.MapGateway();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
