using Auth.Application.Abstractions;
using Auth.Domain.Users;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.Profiles.ProfilePins;

/// <summary>
/// Puts a PIN on a profile of the caller's account, replaces it, or with a <see langword="null"/> PIN takes it off.
/// The password is asked again (a child holding the session must not change it) and counts as a sign-in attempt
/// when wrong. Every sign-in that had opened the profile has to enter the new PIN.
/// </summary>
public sealed record ChangeProfilePinCommand(Guid UserId, Guid ProfileId, string Password, string? Pin) : ICommand;

/// <summary>Opens a locked profile for one sign-in (<paramref name="SessionId"/>, the token's <c>sid</c>).</summary>
public sealed record UnlockProfileCommand(Guid UserId, Guid SessionId, Guid ProfileId, string Pin) : ICommand;

internal sealed class ChangeProfilePinValidator : IValidator<ChangeProfilePinCommand>
{
    public IEnumerable<ValidationFailure> Validate(ChangeProfilePinCommand instance)
    {
        if (string.IsNullOrEmpty(instance.Password) || instance.Password.Length > PasswordPolicy.MaxLength)
        {
            yield return new ValidationFailure(nameof(instance.Password), UserErrors.PasswordRequired.Code, UserErrors.PasswordRequired.Message);
        }

        if (instance.Pin is not null && !Profile.IsWellFormedPin(instance.Pin))
        {
            yield return new ValidationFailure(nameof(instance.Pin), ProfileErrors.PinInvalid.Code, ProfileErrors.PinInvalid.Message);
        }
    }
}

internal sealed class ChangeProfilePinHandler(
    IUserRepository users,
    IProfileUnlockRepository unlocks,
    IPasswordHasher hasher,
    TimeProvider timeProvider) : ICommandHandler<ChangeProfilePinCommand>
{
    public async Task<Result> Handle(ChangeProfilePinCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        var check = hasher.Verify(user.PasswordHash, command.Password);
        if (user.CanSignIn(now).IsFailure)
        {
            return UserErrors.PasswordIncorrect;
        }

        if (check == PasswordCheck.Failed)
        {
            await users.RecordFailedSignInAsync(user.Id, now, cancellationToken).ConfigureAwait(false);
            return UserErrors.PasswordIncorrect;
        }

        var changed = user.ChangeProfilePin(command.ProfileId, command.Pin is null ? null : hasher.Hash(command.Pin));
        if (changed.IsFailure)
        {
            return changed;
        }

        await unlocks.RemoveForProfileAsync(command.ProfileId, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed class UnlockProfileValidator : IValidator<UnlockProfileCommand>
{
    public IEnumerable<ValidationFailure> Validate(UnlockProfileCommand instance)
    {
        if (!Profile.IsWellFormedPin(instance.Pin))
        {
            yield return new ValidationFailure(nameof(instance.Pin), ProfileErrors.PinInvalid.Code, ProfileErrors.PinInvalid.Message);
        }
    }
}

/// <summary>
/// Checks the PIN; a wrong one is counted at once and, at <see cref="Profile.MaxFailedPins"/>, locks the profile for
/// <see cref="Profile.PinLockoutDuration"/> (409 <see cref="ProfileErrors.PinLocked"/>). A profile without a PIN is
/// open already. The next access token of the sign-in carries the profile in <c>profile_unlocked</c>.
/// </summary>
internal sealed class UnlockProfileHandler(
    IUserRepository users,
    IProfileUnlockRepository unlocks,
    IPasswordHasher hasher,
    TimeProvider timeProvider) : ICommandHandler<UnlockProfileCommand>
{
    public async Task<Result> Handle(UnlockProfileCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        var profile = user?.Profiles.FirstOrDefault(item => item.Id == command.ProfileId);
        if (user is null || profile is null)
        {
            return ProfileErrors.NotFound;
        }

        if (profile.PinHash is not { } pinHash)
        {
            return Result.Success();
        }

        var now = timeProvider.GetUtcNow();
        if (profile.IsPinLockedOut(now))
        {
            return ProfileErrors.PinLocked;
        }

        if (hasher.Verify(pinHash, command.Pin) == PasswordCheck.Failed)
        {
            await users.RecordFailedPinAsync(profile.Id, now, cancellationToken).ConfigureAwait(false);
            return ProfileErrors.PinIncorrect;
        }

        user.RecordRightProfilePin(profile.Id);
        if (!await unlocks.ExistsAsync(command.SessionId, profile.Id, cancellationToken).ConfigureAwait(false))
        {
            unlocks.Add(ProfileUnlock.Create(command.SessionId, profile.Id, now));
        }

        return Result.Success();
    }
}
