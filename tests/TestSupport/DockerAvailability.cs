using DotNet.Testcontainers.Builders;

namespace NetBoxPlus.Testing;

/// <summary>
/// Decides what a missing Docker means for tests that start containers. Linked into every test
/// project that uses Testcontainers.
/// </summary>
internal static class DockerAvailability
{
    /// <summary>Set to <c>true</c> where Docker must be present, such as CI.</summary>
    public const string RequireVariable = "NETBOXPLUS_REQUIRE_DOCKER";

    /// <summary>
    /// Locally, a missing Docker skips the tests (reported as skipped, never as passed). Where
    /// <see cref="RequireVariable"/> is set, it fails them instead, so a runner without Docker
    /// cannot turn the build green by skipping.
    /// </summary>
    public static bool CanSkip(Exception exception)
        => !IsRequired && IsDockerMissing(exception);

    private static bool IsRequired
        => string.Equals(Environment.GetEnvironmentVariable(RequireVariable), "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsDockerMissing(Exception exception)
        => exception is DockerUnavailableException
            || exception.InnerException is DockerUnavailableException
            || exception is TypeInitializationException { InnerException: DockerUnavailableException };
}
