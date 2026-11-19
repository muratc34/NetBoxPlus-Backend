namespace Auth.Migrator;

/// <summary>
/// The first administrator, from configuration (environment variables <c>Auth__Seed__AdminEmail</c>
/// and <c>Auth__Seed__AdminPassword</c>, or user secrets locally). Both or neither.
/// </summary>
internal sealed class AdministratorSeedSettings
{
    public const string SectionName = "Auth:Seed";

    public string? AdminEmail { get; init; }

    public string? AdminPassword { get; init; }
}
