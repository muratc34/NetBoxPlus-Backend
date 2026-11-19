namespace Shared.Infrastructure.API.Hosting;

/// <summary>
/// Tags that decide which health endpoint runs a check. Infrastructure packages tag their
/// dependency checks (database, broker) with <see cref="Ready"/>.
/// </summary>
public static class HealthTags
{
    /// <summary>Checked by <c>/health</c>: the process is up. Never depends on other systems.</summary>
    public const string Live = "live";

    /// <summary>Checked by <c>/health/ready</c>: the service can handle traffic.</summary>
    public const string Ready = "ready";
}
