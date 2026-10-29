using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Sample.API.Notes;
using Sample.Application.Notes.GetNote;
using Sample.Contracts;
using Sample.Infrastructure.Persistence;
using Shared.Application.Messaging;

namespace Sample.IntegrationTests;

[Collection(nameof(SampleApiCollection))]
public sealed class NoteApiTests(SampleApiFixture api)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_Returns201WithTheNewNotesLocation()
    {
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/notes", new CreateNoteRequest("Shopping list", "Milk"), Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreateNoteResponse>(Cancellation);
        Assert.Equal($"/notes/{created!.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Get_ReturnsTheCreatedNote()
    {
        using var client = api.CreateClient();
        var id = await CreateNoteAsync(client, "Reading list");

        var note = await client.GetFromJsonAsync<NoteResponse>($"/notes/{id}", Cancellation);

        Assert.Equal("Reading list", note!.Title);
        Assert.Equal("Content", note.Content);
        Assert.NotEqual(default, note.CreatedAt);
    }

    [Fact]
    public async Task Create_WithABlankTitle_Returns400WithFieldErrors()
    {
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/notes", new CreateNoteRequest(" ", "Milk"), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await Json(response);
        Assert.Equal("Note.TitleRequired", problem.GetProperty("errorCodes").GetProperty("title")[0].GetString());
    }

    [Fact]
    public async Task Create_WithATooLongTitle_Returns400FromTheDomainRule()
    {
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/notes", new CreateNoteRequest(new string('a', 201), "Milk"), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Note.TitleTooLong", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_UnknownNote_Returns404()
    {
        using var client = api.CreateClient();

        var response = await client.GetAsync($"/notes/{Guid.CreateVersion7()}", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Note.NotFound", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Delete_WithoutAToken_Returns401()
    {
        using var client = api.CreateClient();
        var id = await CreateNoteAsync(client, "Keep me");

        var response = await client.DeleteAsync($"/notes/{id}", Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutThePermission_Returns403()
    {
        using var client = api.CreateClient(api.CreateToken("sample:notes:read"));
        var id = await CreateNoteAsync(client, "Keep me too");

        var response = await client.DeleteAsync($"/notes/{id}", Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithThePermission_SoftDeletesTheNote()
    {
        using var client = api.CreateClient(api.CreateToken(SamplePermissionsForTests.DeleteNotes));
        var id = await CreateNoteAsync(client, "Delete me");

        var deleted = await client.DeleteAsync($"/notes/{id}", Cancellation);
        var afterwards = await client.GetAsync($"/notes/{id}", Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);

        await using var scope = api.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<SampleDbContext>().Notes
            .IgnoreQueryFilters()
            .SingleAsync(n => n.Id == id, Cancellation);
        Assert.True(row.IsDeleted);
        Assert.NotNull(row.DeletedAt);
    }

    [Fact]
    public async Task Create_PublishesNoteCreatedThroughTheOutbox()
    {
        await using var connection = await new ConnectionFactory { Uri = new Uri(api.RabbitConnectionString) }.CreateConnectionAsync(Cancellation);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Cancellation);
        var messageType = IntegrationEvents.MessageTypeOf(typeof(NoteCreated));

        // A private queue bound the way a consuming service would bind its own.
        await channel.ExchangeDeclareAsync("netboxplus.sample.events", ExchangeType.Topic, durable: true, cancellationToken: Cancellation);
        var queue = (await channel.QueueDeclareAsync(cancellationToken: Cancellation)).QueueName;
        await channel.QueueBindAsync(queue, "netboxplus.sample.events", messageType, cancellationToken: Cancellation);

        using var client = api.CreateClient();
        var id = await CreateNoteAsync(client, "Announce me");

        var delivery = await WaitForMessageAsync(channel, queue, payload => payload.Contains(id.ToString(), StringComparison.Ordinal));

        Assert.Equal(messageType, delivery.BasicProperties.Type);
        Assert.Equal(
            new NoteCreated(id, "Announce me"),
            JsonSerializer.Deserialize<NoteCreated>(delivery.Body.Span, IntegrationEvents.SerializerOptions));
    }

    [Fact]
    public async Task Readiness_IsHealthyAgainstRealDependencies()
    {
        using var client = api.CreateClient();

        var response = await client.GetAsync("/health/ready", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> CreateNoteAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/notes", new CreateNoteRequest(title, "Content"), Cancellation);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateNoteResponse>(Cancellation))!.Id;
    }

    private static async Task<BasicGetResult> WaitForMessageAsync(IChannel channel, string queue, Func<string, bool> matches)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var message = await channel.BasicGetAsync(queue, autoAck: true, timeout.Token);
            if (message is not null && matches(Encoding.UTF8.GetString(message.Body.Span)))
            {
                return message;
            }

            await Task.Delay(100, timeout.Token);
        }
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);

    /// <summary>Mirrors the permission the API requires; a typo here makes the test fail, not pass.</summary>
    private static class SamplePermissionsForTests
    {
        public const string DeleteNotes = "sample:notes:delete";
    }
}
