using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Shared.Application.Tests;

public sealed class DispatcherTests
{
    [Fact]
    public async Task Send_Command_RunsItsHandler()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        var result = await Dispatcher(scope).Send(new PingCommand(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["handler"], Log(scope).Entries);
    }

    [Fact]
    public async Task Send_CommandWithResponse_ReturnsTheHandlersValue()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        var result = await Dispatcher(scope).Send(new CreateItemCommand("Book", 1), TestContext.Current.CancellationToken);

        Assert.Equal(CreateItemHandler.CreatedId, result.Value);
    }

    [Fact]
    public async Task Query_ReturnsTheHandlersValue()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        var result = await Dispatcher(scope).Query(new GetItemQuery(7), TestContext.Current.CancellationToken);

        Assert.Equal("item-7", result.Value);
    }

    [Fact]
    public async Task Send_WhenTheHandlerFails_ReturnsItsError()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        var result = await Dispatcher(scope).Send(new FailingCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(FailingHandler.Conflict, result.Error);
    }

    [Fact]
    public async Task Send_WhenTheHandlerThrows_PropagatesTheException()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Dispatcher(scope).Send(new ThrowingCommand(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Send_WithoutAHandler_ThrowsNamingTheRequest()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Dispatcher(scope).Send(new UnhandledCommand(), TestContext.Current.CancellationToken));

        Assert.Contains(nameof(UnhandledCommand), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_PassesTheCancellationTokenToTheHandler()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();

        var result = await Dispatcher(scope).Query(new CancellationAwareQuery(), cancellation.Token);

        Assert.True(result.Value);
    }

    [Fact]
    public async Task Behaviors_RunInRegistrationOrder_FirstRegisteredOutermost()
    {
        using var provider = TestHost.Build(services =>
        {
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(OuterBehavior<,>));
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(InnerBehavior<,>));
        });
        using var scope = provider.CreateScope();

        await Dispatcher(scope).Send(new PingCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["outer:before", "inner:before", "handler", "inner:after", "outer:after"],
            Log(scope).Entries);
    }

    [Fact]
    public async Task Send_RepeatedCalls_UseTheSameHandlerEachTime()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();
        var dispatcher = Dispatcher(scope);

        await dispatcher.Send(new PingCommand(), TestContext.Current.CancellationToken);
        await dispatcher.Send(new PingCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(["handler", "handler"], Log(scope).Entries);
    }

    private static IDispatcher Dispatcher(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IDispatcher>();

    private static CallLog Log(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<CallLog>();
}
