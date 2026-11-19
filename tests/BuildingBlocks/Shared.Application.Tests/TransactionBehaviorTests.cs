using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;

namespace Shared.Application.Tests;

public sealed class TransactionBehaviorTests
{
    [Fact]
    public async Task SuccessfulCommand_IsSavedOnce()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).Send(new PingCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(1, UnitOfWork(scope).SaveCount);
    }

    [Fact]
    public async Task SuccessfulCommandWithResponse_IsSavedOnce()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).Send(new CreateItemCommand("Book", 1), TestContext.Current.CancellationToken);

        Assert.Equal(1, UnitOfWork(scope).SaveCount);
    }

    [Fact]
    public async Task FailedCommand_IsNotSaved()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).Send(new FailingCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(0, UnitOfWork(scope).SaveCount);
    }

    [Fact]
    public async Task ThrowingCommand_IsNotSaved()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Dispatcher(scope).Send(new ThrowingCommand(), TestContext.Current.CancellationToken));

        Assert.Equal(0, UnitOfWork(scope).SaveCount);
    }

    [Fact]
    public async Task SaveThatLosesARace_IsAConflict_NotAnException()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();
        UnitOfWork(scope).Conflicts = true;

        var plain = await Dispatcher(scope).Send(new PingCommand(), TestContext.Current.CancellationToken);
        var withResponse = await Dispatcher(scope).Send(new CreateItemCommand("Book", 1), TestContext.Current.CancellationToken);

        Assert.Equal("Data.Conflict", plain.Error.Code);
        Assert.Equal(Shared.Domain.ErrorType.Conflict, plain.Error.Type);
        Assert.Equal("Data.Conflict", withResponse.Error.Code);
    }

    [Fact]
    public async Task Query_IsNeverSaved()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).Query(new GetItemQuery(1), TestContext.Current.CancellationToken);

        Assert.Equal(0, UnitOfWork(scope).SaveCount);
    }

    private static IDispatcher Dispatcher(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IDispatcher>();

    private static FakeUnitOfWork UnitOfWork(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<FakeUnitOfWork>();
}
