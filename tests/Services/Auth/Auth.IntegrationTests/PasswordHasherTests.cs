using Auth.Application.Abstractions;
using Auth.Domain.Users;
using Auth.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Auth.IntegrationTests;

/// <summary>The real hasher; no database needed.</summary>
public sealed class PasswordHasherTests
{
    private readonly IdentityPasswordHasher _hasher = new(Options.Create(new PasswordHasherOptions
    {
        IterationCount = IdentityPasswordHasher.IterationCount,
    }));

    [Fact]
    public void Hash_VerifiesOnlyTheSamePassword()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.Equal(PasswordCheck.Succeeded, _hasher.Verify(hash, "correct horse battery staple"));
        Assert.Equal(PasswordCheck.Failed, _hasher.Verify(hash, "correct horse battery stapl"));
        Assert.DoesNotContain("correct horse", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void SamePassword_HashesDifferentlyEachTime()
    {
        Assert.NotEqual(_hasher.Hash("correct horse battery staple"), _hasher.Hash("correct horse battery staple"));
    }

    [Fact]
    public void HashWithFewerIterations_AsksForARehash()
    {
        var weaker = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 100_000 }))
            .HashPassword(null!, "correct horse battery staple");

        Assert.Equal(PasswordCheck.SucceededRehashNeeded, _hasher.Verify(weaker, "correct horse battery staple"));
    }

    [Fact]
    public void Hash_UsesTheConfiguredIterationCount()
    {
        // Identity v3 format: 1 version byte, 4 bytes PRF, 4 bytes iteration count (big-endian), ...
        var bytes = Convert.FromBase64String(_hasher.Hash("correct horse battery staple"));
        var iterations = (bytes[5] << 24) | (bytes[6] << 16) | (bytes[7] << 8) | bytes[8];

        Assert.Equal(IdentityPasswordHasher.IterationCount, iterations);
    }
}
