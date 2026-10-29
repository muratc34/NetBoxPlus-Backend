using System.Net;
using System.Net.Http.Json;
using Feed.Application.Items.GetFeed;
using Feed.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sample.Contracts;

namespace Feed.IntegrationTests;

[Collection(nameof(FeedApiCollection))]
public sealed class NoteCreatedConsumerTests(FeedApiFixture api)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    // The inbox key of the handler: its full type name. Written out, because renaming or moving the
    // handler changes the key, and messages it already processed would be processed again.
    private const string Consumer = "Feed.Application.Items.AddFeedItem.NoteCreatedHandler";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NoteCreated_AppearsInTheFeed()
    {
        using var client = api.CreateClient();
        var noteId = Guid.CreateVersion7();

        await api.PublishAsync(new NoteCreated(noteId, "Shopping list"), Guid.CreateVersion7());

        await EventuallyAsync(async () =>
        {
            var feed = await client.GetFromJsonAsync<List<FeedItemResponse>>("/items?limit=100", Cancellation);
            return feed!.Exists(item => item.NoteId == noteId && item.Title == "Shopping list");
        });
    }

    [Fact]
    public async Task SameMessageTwice_IsSkippedByTheInbox()
    {
        var noteId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();

        await api.PublishAsync(new NoteCreated(noteId, "Reading list"), messageId);
        await EventuallyAsync(async () => (await CountAsync(noteId, messageId)).Inbox == 1);

        await api.PublishAsync(new NoteCreated(noteId, "Reading list"), messageId);
        await EventuallyAsync(() => Task.FromResult(api.Logs.GetSnapshot().Any(record =>
            record.Message.Contains($"already processed message {messageId}", StringComparison.Ordinal))));

        Assert.Equal((Items: 1, Inbox: 1), await CountAsync(noteId, messageId));
    }

    [Fact]
    public async Task SameNoteInTwoMessages_AddsOneItem()
    {
        var noteId = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        await api.PublishAsync(new NoteCreated(noteId, "Packing list"), first);
        await api.PublishAsync(new NoteCreated(noteId, "Packing list"), second);

        // Both messages are processed (each has its inbox record); the handler adds the note once.
        await EventuallyAsync(async () => (await CountAsync(noteId, first)).Inbox == 1 && (await CountAsync(noteId, second)).Inbox == 1);
        Assert.Equal(1, (await CountAsync(noteId, first)).Items);
    }

    [Fact]
    public async Task InvalidEvent_IsRecordedAsProcessedWithoutAnItem()
    {
        var noteId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();

        await api.PublishAsync(new NoteCreated(noteId, " "), messageId);

        // A rejected message is not retried: the inbox keeps it, the feed stays unchanged.
        await EventuallyAsync(async () => (await CountAsync(noteId, messageId)).Inbox == 1);
        Assert.Equal(0, (await CountAsync(noteId, messageId)).Items);
    }

    [Fact]
    public async Task GetFeed_WithALimitOutOfRange_Returns400()
    {
        using var client = api.CreateClient();

        var response = await client.GetAsync(new Uri("/items?limit=0", UriKind.Relative), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<(int Items, int Inbox)> CountAsync(Guid noteId, Guid messageId)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FeedDbContext>();

        var items = await context.Items.CountAsync(item => item.NoteId == noteId, Cancellation);
        var inbox = await context.InboxMessages.CountAsync(message => message.MessageId == messageId && message.Consumer == Consumer, Cancellation);

        return (items, inbox);
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"The condition was not met within {Timeout.TotalSeconds} seconds.");
            await Task.Delay(TimeSpan.FromMilliseconds(100), Cancellation);
        }
    }
}
