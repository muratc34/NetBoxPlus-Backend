using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Events;
using Shared.Domain;

namespace Shared.Application.Tests;

public sealed class DomainEventDispatcherTests
{
    [Fact]
    public async Task Dispatch_CallsEveryHandlerOfEachEventInOrder()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        await Dispatcher(scope).DispatchAsync([new ItemCreated(first), new ItemCreated(second)], TestContext.Current.CancellationToken);

        Assert.Equal(
            [$"first:{first}", $"second:{first}", $"first:{second}", $"second:{second}"],
            scope.ServiceProvider.GetRequiredService<CallLog>().Entries);
    }

    [Fact]
    public async Task Dispatch_EventWithoutHandlers_IsIgnored()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).DispatchAsync([new NobodyListens()], TestContext.Current.CancellationToken);

        Assert.Empty(scope.ServiceProvider.GetRequiredService<CallLog>().Entries);
    }

    [Fact]
    public async Task Dispatch_NoEvents_DoesNothing()
    {
        using var provider = TestHost.Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).DispatchAsync(Array.Empty<IDomainEvent>(), TestContext.Current.CancellationToken);

        Assert.Empty(scope.ServiceProvider.GetRequiredService<CallLog>().Entries);
    }

    private static IDomainEventDispatcher Dispatcher(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();
}
