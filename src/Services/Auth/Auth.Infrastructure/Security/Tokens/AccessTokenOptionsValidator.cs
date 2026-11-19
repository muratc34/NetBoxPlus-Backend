using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security.Tokens;

/// <summary>
/// Runs at startup: the service does not start without a usable signing key. Messages name key ids
/// only, never key material.
/// </summary>
internal sealed class AccessTokenOptionsValidator : IValidateOptions<AccessTokenOptions>
{
    private const string Section = AccessTokenOptions.SectionName;

    public ValidateOptionsResult Validate(string? name, AccessTokenOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add($"{Section}:{nameof(AccessTokenOptions.Issuer)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add($"{Section}:{nameof(AccessTokenOptions.Audience)} is required.");
        }

        if (options.AccessTokenLifetime < TimeSpan.FromMinutes(1) || options.AccessTokenLifetime > TimeSpan.FromHours(1))
        {
            failures.Add($"{Section}:{nameof(AccessTokenOptions.AccessTokenLifetime)} must be between one minute and one hour.");
        }

        if (options.SigningKeys.Count == 0)
        {
            failures.Add($"{Section}:{nameof(AccessTokenOptions.SigningKeys)} has no keys. Locally, run ./deploy/dev.ps1 secrets.");
        }

        foreach (var (keyId, pem) in options.SigningKeys)
        {
            if (!SigningKeyRing.IsValidKeyId(keyId))
            {
                failures.Add($"Signing key id '{keyId}' must be 1-64 letters, digits, '.', '_' or '-'.");
            }
            else if (!SigningKeyRing.IsValidKey(pem))
            {
                failures.Add($"Signing key '{keyId}' must be a P-256 private key in PEM.");
            }
        }

        if (options.SigningKeys.Count > 0 && !options.SigningKeys.ContainsKey(options.ActiveKeyId))
        {
            failures.Add($"{Section}:{nameof(AccessTokenOptions.ActiveKeyId)} must name one of the signing keys.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
