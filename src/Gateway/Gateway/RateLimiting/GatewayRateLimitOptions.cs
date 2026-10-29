namespace Gateway.RateLimiting;

/// <summary>
/// Request limits per client, per route group. Bound from the <c>RateLimiting</c> section. The
/// counters live in the gateway's memory, so each instance counts on its own.
/// </summary>
public sealed class GatewayRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public FixedWindowLimit Public { get; set; } = new() { PermitLimit = 100, Window = TimeSpan.FromSeconds(10) };

    /// <summary>Stricter than <see cref="Public"/>: the admin panel makes far fewer calls.</summary>
    public FixedWindowLimit Admin { get; set; } = new() { PermitLimit = 30, Window = TimeSpan.FromSeconds(10) };
}

/// <summary>At most <see cref="PermitLimit"/> requests per <see cref="Window"/>.</summary>
public sealed class FixedWindowLimit
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }

    internal bool IsValid => PermitLimit > 0 && Window > TimeSpan.Zero;
}
