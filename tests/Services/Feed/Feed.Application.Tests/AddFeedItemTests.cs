using Feed.Application.Items.AddFeedItem;
using Feed.Application.Items.GetFeed;
using Feed.Domain.Items;
using Sample.Contracts;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Feed.Application.Tests;

public sealed class AddFeedItemTests
{
    private readonly FakeFeedItemRepository _items = new();

    [Fact]
    public async Task NewNote_IsAdded()
    {
        var noteId = Guid.CreateVersion7();

        var result = await new AddFeedItemHandler(_items).Handle(new AddFeedItemCommand(noteId, "Shopping list"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(_items.Items);
        Assert.Equal(noteId, item.NoteId);
        Assert.Equal("Shopping list", item.Title);
    }

    [Fact]
    public async Task NoteAlreadyInTheFeed_SucceedsWithoutAddingAgain()
    {
        var noteId = Guid.CreateVersion7();
        _items.Add(FeedItem.Create(noteId, "Shopping list").Value);

        var result = await new AddFeedItemHandler(_items).Handle(new AddFeedItemCommand(noteId, "Shopping list"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(_items.Items);
    }

    [Fact]
    public async Task TooLongTitle_FailsWithoutAdding()
    {
        var result = await new AddFeedItemHandler(_items).Handle(
            new AddFeedItemCommand(Guid.CreateVersion7(), new string('a', FeedItem.TitleMaxLength + 1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(FeedItemErrors.TitleTooLong, result.Error);
        Assert.Empty(_items.Items);
    }

    [Fact]
    public void Validator_ReportsMissingFields()
    {
        var failures = new AddFeedItemValidator().Validate(new AddFeedItemCommand(Guid.Empty, " ")).ToList();

        Assert.Equal(["NoteId", "Title"], failures.Select(failure => failure.PropertyName));
    }

    [Fact]
    public async Task NoteCreated_IsSentAsAnAddFeedItemCommand()
    {
        var dispatcher = new FakeDispatcher();
        var noteId = Guid.CreateVersion7();

        await new NoteCreatedHandler(dispatcher).Handle(new NoteCreated(noteId, "Shopping list"), TestContext.Current.CancellationToken);

        Assert.Equal(new AddFeedItemCommand(noteId, "Shopping list"), Assert.Single(dispatcher.Sent));
    }

    [Fact]
    public async Task RejectedCommand_IsTheHandlersResult()
    {
        var dispatcher = new FakeDispatcher { Result = FeedItemErrors.TitleRequired };

        var result = await new NoteCreatedHandler(dispatcher).Handle(new NoteCreated(Guid.CreateVersion7(), " "), TestContext.Current.CancellationToken);

        Assert.Equal(FeedItemErrors.TitleRequired, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(GetFeedQuery.MaxLimit + 1)]
    public void GetFeedValidator_RejectsALimitOutOfRange(int limit)
    {
        Assert.Equal("Feed.LimitOutOfRange", Assert.Single(new GetFeedValidator().Validate(new GetFeedQuery(limit))).Code);
    }

    private sealed class FakeFeedItemRepository : IFeedItemRepository
    {
        public List<FeedItem> Items { get; } = [];

        public Task<bool> ExistsForNoteAsync(Guid noteId, CancellationToken cancellationToken)
            => Task.FromResult(Items.Exists(item => item.NoteId == noteId));

        public void Add(FeedItem item) => Items.Add(item);
    }

    private sealed class FakeDispatcher : IDispatcher
    {
        public List<ICommand> Sent { get; } = [];

        public Result Result { get; init; } = Result.Success();

        public Task<Result> Send(ICommand command, CancellationToken cancellationToken = default)
        {
            Sent.Add(command);
            return Task.FromResult(Result);
        }

        public Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<TResponse>> Query<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
