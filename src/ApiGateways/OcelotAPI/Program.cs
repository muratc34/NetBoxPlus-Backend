using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Shared;
using Shared.Security.Jwt;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("ocelot.json");

builder.Services.AddCors();

//Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var tokenOptions = builder.Configuration.GetSection("TokenOptions").Get<TokenOptions>();
builder.Services.AddCustomJwtAuthentication(tokenOptions!);

builder.Services.AddOcelot();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(builder => builder.WithOrigins("http://localhost:3000/").AllowAnyHeader());

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseOcelot(new OcelotPipelineConfiguration {
    AuthenticationMiddleware = async (cpt, est) =>
    {
        await est.Invoke();
    }
});

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();