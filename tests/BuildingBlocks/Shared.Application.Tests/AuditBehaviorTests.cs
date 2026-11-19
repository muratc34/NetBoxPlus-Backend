using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;

namespace Shared.Application.Tests;

public sealed class AuditBehaviorTests
{
    [Fact]
    public async Task SuccessfulAuditedCommand_IsRecordedWithActorTargetAndCommand()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();
        var command = new RenameItemCommand(Guid.CreateVersion7(), "Book");

        await Dispatcher(scope).Send(command, TestContext.Current.CancellationToken);

        var (entry, _) = Assert.Single(AuditLog(scope).Entries);
        Assert.Equal("RenameItem", entry.Action);
        Assert.Equal(FakeCurrentUser.UserId, entry.ActorId);
        Assert.Equal(command.ItemId, entry.TargetId);
        Assert.Same(command, entry.Details);
    }

    [Fact]
    public async Task Entry_IsAddedBeforeTheSave_SoItIsSavedWithTheChange()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).Send(new RenameItemCommand(Guid.CreateVersion7(), "Book"), TestContext.Current.CancellationToken);

        Assert.Equal(0, Assert.Single(AuditLog(scope).Entries).SavesBefore);
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<FakeUnitOfWork>().SaveCount);
    }

    [Fact]
    public async Task CreatingCommand_RecordsTheReturnedIdAsTarget()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).Send(new RegisterItemCommand("Book"), TestContext.Current.CancellationToken);

        Assert.Equal(RegisterItemHandler.RegisteredId, Assert.Single(AuditLog(scope).Entries).Entry.TargetId);
    }

    [Fact]
    public async Task FailedAuditedCommand_RecordsNothing()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).Send(new RejectedAuditedCommand(Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        Assert.Empty(AuditLog(scope).Entries);
    }

    [Fact]
    public async Task CommandThatIsNotAudited_RecordsNothing()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).Send(new PingCommand(), TestContext.Current.CancellationToken);

        Assert.Empty(AuditLog(scope).Entries);
    }

    private static IDispatcher Dispatcher(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IDispatcher>();

    private static FakeAuditLog AuditLog(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<FakeAuditLog>();
}
