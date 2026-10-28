using Microsoft.Extensions.Compliance.Classification;

namespace Shared.Infrastructure.API.Logging;

/// <summary>
/// Data classes that must never appear in logs as-is. A <c>[LoggerMessage]</c> parameter marked
/// with one of the attributes below is erased by the redaction configured in
/// <see cref="Hosting.ServiceDefaultsExtensions.AddServiceDefaults{TBuilder}"/>.
/// </summary>
public static class DataClassifications
{
    public const string TaxonomyName = "NetBoxPlus";

    /// <summary>Data that identifies a person: email address, name, phone number.</summary>
    public static DataClassification Personal { get; } = new(TaxonomyName, nameof(Personal));

    /// <summary>Credentials and secrets: passwords, tokens, keys.</summary>
    public static DataClassification Secret { get; } = new(TaxonomyName, nameof(Secret));
}

/// <summary>Marks a logged value as personal data; it is erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PersonalDataAttribute : DataClassificationAttribute
{
    public PersonalDataAttribute()
        : base(DataClassifications.Personal)
    {
    }
}

/// <summary>Marks a logged value as a secret; it is erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SecretDataAttribute : DataClassificationAttribute
{
    public SecretDataAttribute()
        : base(DataClassifications.Secret)
    {
    }
}
