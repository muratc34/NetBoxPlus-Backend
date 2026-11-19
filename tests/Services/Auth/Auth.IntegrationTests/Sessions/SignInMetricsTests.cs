using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Auth.API.Sessions;
using Auth.Application;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Auth.IntegrationTests.Sessions;

/// <summary>Every sign-in answer looks the same to the caller; the metric tells them apart for the operator.</summary>
[Collection(nameof(AuthApiCollection))]
public sealed class SignInMetricsTests(AuthApiFixture api)
{
    private const string Password = "correct horse battery staple";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SignIns_AreCountedByKindAndResult()
    {
        Assert.SkipUnless(api.IsAvailable, api.SkipReason);
        using var client = api.CreateClient();
        await using var scope = api.CreateScope();
        using var signIns = new MetricCollector<long>(scope.ServiceProvider.GetRequiredService<IMeterFactory>(), AuthMetrics.MeterName, "netboxplus.auth.sign_ins");
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var disabled = $"user-{Guid.NewGuid():N}@example.com";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/register", new RegisterRequest(email, Password), Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/register", new RegisterRequest(disabled, Password), Cancellation)).StatusCode);
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users
            .Where(user => user.Email == Auth.Domain.Users.EmailAddress.Create(disabled).Value)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsDisabled, true), Cancellation);

        await client.PostAsJsonAsync("/sign-in", new SignInRequest(email, Password), Cancellation);
        await client.PostAsJsonAsync("/sign-in", new SignInRequest(email, "wrong password, nothing like it"), Cancellation);
        await client.PostAsJsonAsync("/sign-in", new SignInRequest($"nobody-{Guid.NewGuid():N}@example.com", Password), Cancellation);
        await client.PostAsJsonAsync("/sign-in", new SignInRequest(disabled, Password), Cancellation);
        await client.PostAsJsonAsync("/sign-in/admin", new SignInRequest(email, Password), Cancellation);

        Assert.Equal(1, Count(signIns, "customer", "succeeded"));
        Assert.Equal(2, Count(signIns, "customer", "failed"));
        Assert.Equal(1, Count(signIns, "customer", "refused"));
        Assert.Equal(1, Count(signIns, "admin", "failed"));
    }

    private static long Count(MetricCollector<long> collector, string kind, string result)
        => collector.GetMeasurementSnapshot()
            .Where(measurement => Equals(measurement.Tags["kind"], kind) && Equals(measurement.Tags["result"], result))
            .Sum(measurement => measurement.Value);
}
