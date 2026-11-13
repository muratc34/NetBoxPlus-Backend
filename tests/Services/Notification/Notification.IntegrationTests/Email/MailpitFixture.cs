using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NetBoxPlus.Testing;

namespace Notification.IntegrationTests.Email;

/// <summary>
/// Mailpit, as in deploy/compose.yaml: it keeps every e-mail it gets and shows them through its API.
/// It accepts only addresses at example.com, so that a refused recipient can be tried too.
/// </summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    public const string AcceptedDomain = "example.com";

    private IContainer? _container;

    public bool IsAvailable { get; private set; }

    public string SkipReason { get; private set; } = string.Empty;

    public string SmtpHost => _container!.Hostname;

    public int SmtpPort => _container!.GetMappedPublicPort(1025);

    public HttpClient Api { get; } = new();

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder("axllent/mailpit:v1.32.0")
                .WithEnvironment("MP_SMTP_ALLOWED_RECIPIENTS", "@example\\.com$")
                .WithPortBinding(1025, assignRandomHostPort: true)
                .WithPortBinding(8025, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025).ForPath("/readyz")))
                .Build();
            await _container.StartAsync();
            Api.BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(8025)}/");
            IsAvailable = true;
        }
        catch (Exception exception) when (DockerAvailability.CanSkip(exception))
        {
            SkipReason = $"Docker is not available: {exception.Message}";
        }
    }

    /// <summary>The e-mail sent to the address, waiting a moment for it to arrive.</summary>
    public async Task<Received?> FindAsync(string to, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var list = await Api.GetFromJsonAsync<MessageList>($"api/v1/search?query={Uri.EscapeDataString("to:" + to)}", cancellationToken);
            if (list?.Messages.FirstOrDefault() is { } summary)
            {
                return await Api.GetFromJsonAsync<Received>($"api/v1/message/{summary.Id}", cancellationToken);
            }

            await Task.Delay(100, cancellationToken);
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        Api.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public sealed record MessageList([property: JsonPropertyName("messages")] IReadOnlyList<MessageSummary> Messages);

    public sealed record MessageSummary([property: JsonPropertyName("ID")] string Id);

    public sealed record Address([property: JsonPropertyName("Name")] string Name, [property: JsonPropertyName("Address")] string Email);

    public sealed record Received(
        [property: JsonPropertyName("From")] Address From,
        [property: JsonPropertyName("To")] IReadOnlyList<Address> To,
        [property: JsonPropertyName("Subject")] string Subject,
        [property: JsonPropertyName("HTML")] string Html,
        [property: JsonPropertyName("Text")] string Text);
}

[CollectionDefinition(nameof(MailpitCollection))]
public sealed class MailpitCollection : ICollectionFixture<MailpitFixture>;
