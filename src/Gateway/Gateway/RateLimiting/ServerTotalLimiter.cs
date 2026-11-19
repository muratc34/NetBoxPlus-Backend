using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Gateway.RateLimiting;

/// <summary>
/// The web application's server's total: one limiter for everything it sends on public routes,
/// shared by all its partitions and disposed with the container.
/// </summary>
internal sealed class ServerTotalLimiter(IOptions<GatewayRateLimitOptions> options) : IDisposable
{
    public RateLimiter Limiter { get; } = new FixedWindowRateLimiter(RateLimitingServiceCollectionExtensions.FixedWindowOptions(options.Value.Server));

    public void Dispose() => Limiter.Dispose();
}

/// <summary>
/// A partition's own limit (a visitor's; none for the server itself), then the shared total. A request
/// the partition refuses never reaches the total, so a visitor over their limit cannot use up what the
/// others share. The rate limiter drops idle partitions; that disposes the partition's own limiter only.
/// </summary>
/// <remarks>
/// ASP.NET Core tries a refused request once more, so while the total is used up a visitor's request
/// counts twice against their own limit. That only happens when the server is over its total anyway.
/// </remarks>
internal sealed class WithinTotalLimiter(RateLimiter? own, RateLimiter total) : RateLimiter
{
    public override TimeSpan? IdleDuration => (own ?? total).IdleDuration;

    public override RateLimiterStatistics? GetStatistics() => (own ?? total).GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        if (own is not null)
        {
            var lease = own.AttemptAcquire(permitCount);
            if (!lease.IsAcquired)
            {
                return lease;
            }

            // A fixed window keeps the permit whether or not the lease stays open.
            lease.Dispose();
        }

        return total.AttemptAcquire(permitCount);
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        if (own is not null)
        {
            var lease = await own.AcquireAsync(permitCount, cancellationToken).ConfigureAwait(false);
            if (!lease.IsAcquired)
            {
                return lease;
            }

            lease.Dispose();
        }

        return await total.AcquireAsync(permitCount, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            own?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (own is not null)
        {
            await own.DisposeAsync().ConfigureAwait(false);
        }
    }
}
