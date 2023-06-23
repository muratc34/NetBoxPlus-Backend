using MassTransit;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.DependencyInjection;
using SubscriptionAPI.Infrastructure.Repositories;
using SubscriptionAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.


builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();

builder.Services.AddScoped<IPlanService, PlanService>();
builder.Services.AddScoped<IPlanRepository, PlanRepository>();

builder.Services.AddTransient<SubscriptionThreadService>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMassTransit(options => {
    options.UsingRabbitMq((context, cfg) => {
        cfg.Host(new Uri("rabbitmq://localhost:5672"), x =>
        {
            x.Username("guest");
            x.Password("guest");
        });
    });

});

var app = builder.Build();

var membershipExpirationTask = app.Services.GetRequiredService<SubscriptionThreadService>();
membershipExpirationTask.CheckSubscriptionExpiration();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
