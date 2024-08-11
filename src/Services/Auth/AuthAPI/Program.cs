using Auth.Application;
using Auth.Infrastructure;
using Auth.Infrastructure.Security;
using Auth.Persistence;
using Shared.Middleware;

var builder = WebApplication.CreateBuilder(args);

var env = builder.Environment;
var configuration = builder.Configuration;
var tokenOptions = configuration.GetSection("TokenOptions").Get<TokenOptions>();

configuration
    .SetBasePath(env.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

// Add services to the container.
builder.Services
    .AddApplication()
    .AddInfrastructure(tokenOptions!)
    .AddPersistence(configuration);

builder.Services.AddHttpContextAccessor();

builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlerMiddleware>();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
