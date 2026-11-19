using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notification.Application.Abstractions;
using Notification.Infrastructure;
using Notification.Infrastructure.Email;

namespace Notification.IntegrationTests.Email;

/// <summary>Brevo's API is not called here: a handler stands in for it and answers as it would.</summary>
public sealed class BrevoEmailSenderTests
{
    private const string ApiKey = "xkeysib-test-not-a-real-key";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheEmail_GoesToBrevosApi_WithTheKeyInItsHeader()
    {
        var brevo = new FakeBrevo(HttpStatusCode.Created);

        var delivery = await Sender(brevo, out _).SendAsync(new EmailMessage("viewer@example.com", "Hoş geldin", "<p>Merhaba</p>", "Merhaba"), Cancellation);

        Assert.Equal(EmailDelivery.Sent, delivery);
        var request = Assert.Single(brevo.Requests);
        Assert.Equal((HttpMethod.Post, "https://api.brevo.com/v3/smtp/email"), (request.Method, request.Uri));
        Assert.Equal(ApiKey, request.ApiKey);
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal(("no-reply@netboxplus.test", "NetBoxPlus"), (root.GetProperty("sender").GetProperty("email").GetString(), root.GetProperty("sender").GetProperty("name").GetString()));
        Assert.Equal("viewer@example.com", root.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.False(root.GetProperty("to")[0].TryGetProperty("name", out _));
        Assert.Equal(("Hoş geldin", "<p>Merhaba</p>", "Merhaba"), (root.GetProperty("subject").GetString(), root.GetProperty("htmlContent").GetString(), root.GetProperty("textContent").GetString()));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, EmailDeliveryOutcome.Rejected)]
    [InlineData(HttpStatusCode.Unauthorized, EmailDeliveryOutcome.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, EmailDeliveryOutcome.Rejected)]
    [InlineData(HttpStatusCode.TooManyRequests, EmailDeliveryOutcome.TryAgain)]
    [InlineData(HttpStatusCode.InternalServerError, EmailDeliveryOutcome.TryAgain)]
    [InlineData(HttpStatusCode.ServiceUnavailable, EmailDeliveryOutcome.TryAgain)]
    public async Task BrevosAnswer_DecidesWhetherToTryAgain(HttpStatusCode status, EmailDeliveryOutcome outcome)
    {
        var delivery = await Sender(new FakeBrevo(status), out var logs).SendAsync(new EmailMessage("viewer@example.com", "Konu", "<p>x</p>", "x"), Cancellation);

        Assert.Equal(outcome, delivery.Outcome);
        Assert.Equal($"Brevo {(int)status}", delivery.Detail);
        Assert.DoesNotContain(logs.Lines, line => line.Contains(ApiKey, StringComparison.Ordinal) || line.Contains("viewer@example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BrevoOutOfReach_IsTriedAgain()
    {
        var delivery = await Sender(new FakeBrevo(null), out var logs).SendAsync(new EmailMessage("viewer@example.com", "Konu", "<p>x</p>", "x"), Cancellation);

        Assert.Equal(EmailDeliveryOutcome.TryAgain, delivery.Outcome);
        Assert.Single(logs.Lines);
    }

    [Theory]
    [InlineData("Brevo", "NetBoxPlus <no-reply@netboxplus.test>", null, null, false)]
    [InlineData("Brevo", "NetBoxPlus <no-reply@netboxplus.test>", ApiKey, null, true)]
    [InlineData("Smtp", "no-reply@netboxplus.test", null, "localhost", true)]
    [InlineData("Smtp", "no-reply@netboxplus.test", null, null, false)]
    [InlineData("Smtp", "not an address", null, "localhost", false)]
    [InlineData("Smtp", null, null, "localhost", false)]
    public void TheSettings_MustBeEnoughToSend(string provider, string? from, string? apiKey, string? host, bool valid)
    {
        var services = Services(new Dictionary<string, string?> { ["Email:Provider"] = provider, ["Email:From"] = from, ["Email:Brevo:ApiKey"] = apiKey, ["Email:Smtp:Host"] = host });

        void Read() => _ = services.GetRequiredService<IOptions<EmailOptions>>().Value;

        if (valid)
        {
            Read();
        }
        else
        {
            Assert.Throws<OptionsValidationException>(Read);
        }
    }

    [Fact]
    public void TheProviderSetting_PicksTheSender()
    {
        Assert.IsType<BrevoEmailSender>(Services(new Dictionary<string, string?> { ["Email:Provider"] = "Brevo", ["Email:From"] = "a@b.test", ["Email:Brevo:ApiKey"] = ApiKey }).GetRequiredService<IEmailSender>());
        Assert.IsType<SmtpEmailSender>(Services(new Dictionary<string, string?> { ["Email:From"] = "a@b.test", ["Email:Smtp:Host"] = "localhost" }).GetRequiredService<IEmailSender>());
    }

    private static ServiceProvider Services(Dictionary<string, string?> settings, FakeBrevo? brevo = null, CapturingLoggerProvider? logs = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (logs is not null)
            {
                logging.AddProvider(logs);
            }
        });
        services.AddEmail(configuration);
        if (brevo is not null)
        {
            services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = brevo));
        }

        return services.BuildServiceProvider();
    }

    private static IEmailSender Sender(FakeBrevo brevo, out CapturingLoggerProvider logs)
    {
        logs = new CapturingLoggerProvider();
        return Services(
            new Dictionary<string, string?> { ["Email:Provider"] = "Brevo", ["Email:From"] = "NetBoxPlus <no-reply@netboxplus.test>", ["Email:Brevo:ApiKey"] = ApiKey },
            brevo,
            logs).GetRequiredService<IEmailSender>();
    }

    private sealed record SeenRequest(HttpMethod Method, string Uri, string? ApiKey, string Body);

    /// <summary>Answers with the status, or fails as an unreachable host when there is none.</summary>
    private sealed class FakeBrevo(HttpStatusCode? status) : HttpMessageHandler
    {
        public List<SeenRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SeenRequest(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.TryGetValues("api-key", out var values) ? values.Single() : null, body));
            return status is { } code
                ? new HttpResponseMessage(code) { Content = new StringContent("{\"code\":\"x\",\"message\":\"y\"}") }
                : throw new HttpRequestException("No such host is known.");
        }
    }

    internal sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capturing(Lines);

        public void Dispose()
        {
        }

        private sealed class Capturing(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    lines.Add(formatter(state, exception) + " " + string.Join(' ', (state as IEnumerable<KeyValuePair<string, object?>>)?.Select(pair => $"{pair.Key}={pair.Value}") ?? []));
                }
            }
        }
    }
}
