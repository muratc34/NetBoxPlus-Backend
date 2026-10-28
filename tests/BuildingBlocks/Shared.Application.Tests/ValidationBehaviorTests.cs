using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Shared.Application.Tests;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task InvalidCommand_ReturnsAllFailuresWithoutCallingTheHandler()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        var result = await Dispatcher(scope).Send(new CreateItemCommand("", 0), TestContext.Current.CancellationToken);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(
            ["CreateItem.NameRequired", "CreateItem.QuantityPositive"],
            error.Failures.Select(f => f.Code).Order(StringComparer.Ordinal));
        Assert.Empty(Log(scope).Entries);
    }

    [Fact]
    public async Task InvalidCommand_IsNotSaved()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).Send(new CreateItemCommand("", 1), TestContext.Current.CancellationToken);

        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<FakeUnitOfWork>().SaveCount);
    }

    [Fact]
    public async Task InvalidCommandWithoutValue_ReturnsAPlainFailedResult()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        var result = await Dispatcher(scope).Send(new DeleteItemCommand(Guid.Empty), TestContext.Current.CancellationToken);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("DeleteItem.IdRequired", Assert.Single(error.Failures).Code);
    }

    [Fact]
    public async Task InvalidQuery_IsRejected()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        var result = await Dispatcher(scope).Query(new GetItemQuery(0), TestContext.Current.CancellationToken);

        Assert.IsType<ValidationError>(result.Error);
        Assert.Empty(Log(scope).Entries);
    }

    [Fact]
    public async Task ValidRequest_ReachesTheHandler()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        var result = await Dispatcher(scope).Send(new CreateItemCommand("Book", 2), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["handler"], Log(scope).Entries);
    }

    [Fact]
    public async Task RequestWithoutValidators_ReachesTheHandler()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        var result = await Dispatcher(scope).Send(new PingCommand(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    private static IDispatcher Dispatcher(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IDispatcher>();

    private static CallLog Log(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<CallLog>();
}
