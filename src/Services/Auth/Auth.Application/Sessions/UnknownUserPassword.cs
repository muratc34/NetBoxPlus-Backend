using System.Security.Cryptography;
using Auth.Application.Abstractions;

namespace Auth.Application.Sessions;

/// <summary>
/// A hash of a random password nobody knows. Signing in with an unknown e-mail address verifies
/// against it, so the request costs as long as one for an existing account and the response time
/// does not tell which addresses have accounts.
/// </summary>
internal sealed class UnknownUserPassword(IPasswordHasher hasher)
{
    private readonly string _hash = hasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    public void Verify(string password) => _ = hasher.Verify(_hash, password);
}
