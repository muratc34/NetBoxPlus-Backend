using Auth.Domain.Passkeys;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infrastructure.Persistence;

internal sealed class UserPasskeyConfiguration : IEntityTypeConfiguration<UserPasskey>
{
    public void Configure(EntityTypeBuilder<UserPasskey> builder)
    {
        builder.ToTable("UserPasskeys");
        builder.HasKey(passkey => passkey.Id);
        builder.Property(passkey => passkey.Id).ValueGeneratedNever();
        builder.Property(passkey => passkey.CredentialId).HasMaxLength(UserPasskey.MaxCredentialIdLength).IsRequired();
        builder.HasIndex(passkey => passkey.CredentialId).IsUnique();
        builder.Property(passkey => passkey.PublicKey).IsRequired();
        builder.Property(passkey => passkey.Name).HasMaxLength(UserPasskey.MaxNameLength).IsRequired();
        builder.HasIndex(passkey => passkey.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(passkey => passkey.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PasskeyChallengeConfiguration : IEntityTypeConfiguration<PasskeyChallenge>
{
    public void Configure(EntityTypeBuilder<PasskeyChallenge> builder)
    {
        builder.ToTable("PasskeyChallenges");
        builder.HasKey(challenge => challenge.Id);
        builder.Property(challenge => challenge.Id).ValueGeneratedNever();
        builder.Property(challenge => challenge.Ceremony).HasConversion<string>().HasMaxLength(16);
        builder.Property(challenge => challenge.Options).IsRequired();
        builder.HasIndex(challenge => challenge.ExpiresAt);
    }
}

internal sealed class PasskeyRepository(AuthDbContext context) : IPasskeyRepository
{
    public Task<UserPasskey?> GetAsync(Guid userId, Guid passkeyId, CancellationToken cancellationToken)
        => context.Passkeys.FirstOrDefaultAsync(passkey => passkey.Id == passkeyId && passkey.UserId == userId, cancellationToken);

    public Task<UserPasskey?> GetByCredentialIdAsync(byte[] credentialId, CancellationToken cancellationToken)
        => context.Passkeys.FirstOrDefaultAsync(passkey => passkey.CredentialId == credentialId, cancellationToken);

    public Task<int> CountAsync(Guid userId, CancellationToken cancellationToken)
        => context.Passkeys.CountAsync(passkey => passkey.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<byte[]>> CredentialIdsAsync(Guid userId, CancellationToken cancellationToken)
        => await context.Passkeys.Where(passkey => passkey.UserId == userId).Select(passkey => passkey.CredentialId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(UserPasskey passkey) => context.Passkeys.Add(passkey);

    public void Remove(UserPasskey passkey) => context.Passkeys.Remove(passkey);

    public void AddChallenge(PasskeyChallenge challenge) => context.PasskeyChallenges.Add(challenge);

    public async Task<PasskeyChallenge?> TryUseChallengeAsync(Guid challengeId, PasskeyCeremony ceremony, Guid? userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The condition and the change are one statement: of two requests with the same challenge, the second
        // finds UsedAt set and updates nothing.
        var used = await context.PasskeyChallenges
            .Where(challenge => challenge.Id == challengeId
                && challenge.Ceremony == ceremony
                && challenge.UserId == userId
                && challenge.UsedAt == null
                && challenge.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(challenge => challenge.UsedAt, now), cancellationToken)
            .ConfigureAwait(false);

        return used == 1
            ? await context.PasskeyChallenges.AsNoTracking().FirstAsync(challenge => challenge.Id == challengeId, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task RemoveForUserAsync(Guid userId, CancellationToken cancellationToken)
        => context.Passkeys.RemoveRange(await context.Passkeys.Where(passkey => passkey.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false));
}
