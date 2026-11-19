using System.Threading.RateLimiting;
using Gateway.RateLimiting;

namespace Gateway.Tests;

public sealed class WithinTotalLimiterTests
{
    private static FixedWindowRateLimiter Window(int permits)
        => new(new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 });

    [Fact]
    public void ARequestThePartitionRefuses_LeavesTheTotalAlone()
    {
        using var total = Window(5);
        using var visitor = new WithinTotalLimiter(Window(1), total);

        Assert.True(visitor.AttemptAcquire().IsAcquired);
        Assert.False(visitor.AttemptAcquire().IsAcquired);

        Assert.Equal(4, total.GetStatistics()!.CurrentAvailablePermits);
    }

    [Fact]
    public async Task ARequestTheTotalRefuses_IsRefused()
    {
        using var total = Window(1);
        using var first = new WithinTotalLimiter(Window(5), total);
        using var second = new WithinTotalLimiter(Window(5), total);

        Assert.True((await first.AcquireAsync(cancellationToken: TestContext.Current.CancellationToken)).IsAcquired);
        Assert.False((await second.AcquireAsync(cancellationToken: TestContext.Current.CancellationToken)).IsAcquired);
        Assert.False(second.AttemptAcquire().IsAcquired);
    }

    [Fact]
    public async Task DroppingAPartition_KeepsTheSharedTotal()
    {
        using var total = Window(5);
        var own = Window(5);

        new WithinTotalLimiter(own, total).Dispose();
        await new WithinTotalLimiter(Window(5), total).DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => own.AttemptAcquire());
        Assert.True(total.AttemptAcquire().IsAcquired);
    }

    [Fact]
    public void ThePartitionIsIdle_WhenItsOwnLimiterIs()
    {
        using var total = Window(5);
        using var own = Window(5);
        using var visitor = new WithinTotalLimiter(own, total);
        using var server = new WithinTotalLimiter(own: null, total);

        // The busy total must not keep a quiet visitor's partition alive (made-up addresses would pile up).
        total.AttemptAcquire().Dispose();

        Assert.Null(total.IdleDuration);
        Assert.NotNull(visitor.IdleDuration);
        Assert.Null(server.IdleDuration);
    }
}
