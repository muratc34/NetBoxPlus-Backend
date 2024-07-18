using AuthAPI.Consumers;
using AuthAPI.Infrastructure;
using AuthAPI.Infrastructure.Repositories;
using AuthAPI.Security.Jwt;
using AuthAPI.Services;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.

builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IProfileRepository, ProfileRepository>();

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenHelper, JwtHelper>();

builder.Services.AddScoped<AuthContext>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<CreditCardCreatedConsumer>();
    x.AddConsumer<SubscriptionCreatedConsumer>();
    x.AddConsumer<UserSubscriptionRemovedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri("rabbitmq://localhost:5672"), h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
        cfg.ReceiveEndpoint("cc-created-event", e =>
        {
            e.ConfigureConsumer<CreditCardCreatedConsumer>(context);
        });
        cfg.ReceiveEndpoint("subs-created-event", e =>
        {
            e.ConfigureConsumer<SubscriptionCreatedConsumer>(context);
        });
        cfg.ReceiveEndpoint("subs-removed-event", e =>
        {
            e.ConfigureConsumer<UserSubscriptionRemovedConsumer>(context);
        });
    });
});

builder.Services.AddControllers();

    

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(builder => builder.WithOrigins("http://localhost:3000").AllowAnyHeader());

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
