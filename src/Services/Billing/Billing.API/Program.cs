using System.Text.Json.Serialization;
using Billing.API.Admin;
using Billing.API.Checkout;
using Billing.API.Public;
using Billing.API.Webhooks;
using Billing.Application;
using Billing.Infrastructure;
using Shared.Infrastructure.API.Hosting;
using Shared.Infrastructure.API.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddJwtAuthentication();
builder.Services.AddOpenApi();

// Enums as names in JSON: the generated TypeScript client and the admin panel read them directly.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddBillingApplication();

if (!BuildTimeOpenApi.IsGeneratingDocument)
{
    builder.Services.AddBillingInfrastructure(builder.Configuration);
}

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapPlanAdminEndpoints();
app.MapPlanEndpoints();
app.MapCheckoutEndpoints();
app.MapWebhookEndpoints();
app.MapDefaultEndpoints();

app.Run();

/// <summary>Entry point; public so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
