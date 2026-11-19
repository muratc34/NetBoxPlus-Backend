using Auth.Application.Abstractions;
using Auth.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// ASP.NET Core Identity's hasher (PBKDF2 with HMAC-SHA512, random salt, versioned format), the
/// only part of Identity this service uses. A hash made with fewer iterations than configured
/// verifies as <see cref="PasswordCheck.SucceededRehashNeeded"/>, so stored hashes get stronger at
/// the next sign-in.
/// </summary>
internal sealed class IdentityPasswordHasher(IOptions<PasswordHasherOptions> options) : IPasswordHasher
{
    /// <summary>OWASP's current recommendation for PBKDF2-HMAC-SHA512 (Identity's default is 100,000).</summary>
    public const int IterationCount = 210_000;

    private readonly PasswordHasher<User> _hasher = new(options);

    // The user argument exists for hashers that mix in user data; this one ignores it.
    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public PasswordCheck Verify(string passwordHash, string password)
        => _hasher.VerifyHashedPassword(null!, passwordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Succeeded,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SucceededRehashNeeded,
            _ => PasswordCheck.Failed,
        };
}
