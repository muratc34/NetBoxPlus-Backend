using Auth.Application.Abstractions;
using Auth.Domain.Users;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.Users.Register;

public sealed record RegisterCommand(string Email, string Password) : ICommand<Guid>;

internal sealed class RegisterValidator : IValidator<RegisterCommand>
{
    public IEnumerable<ValidationFailure> Validate(RegisterCommand instance)
    {
        var email = EmailAddress.Create(instance.Email);
        if (email.IsFailure)
        {
            yield return new ValidationFailure(nameof(instance.Email), email.Error.Code, email.Error.Message);
        }

        var password = PasswordPolicy.Check(instance.Password);
        if (password.IsFailure)
        {
            yield return new ValidationFailure(nameof(instance.Password), password.Error.Code, password.Error.Message);
        }
    }
}

/// <summary>
/// Creates the account; the caller signs in next. A taken address is reported (409): without
/// e-mail verification there is no way to hide it, so the gateway rate-limits this route.
/// </summary>
internal sealed class RegisterHandler(IUserRepository users, IPasswordHasher hasher) : ICommandHandler<RegisterCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var email = EmailAddress.Create(command.Email).Value;

        if (await users.ExistsByEmailAsync(email, cancellationToken).ConfigureAwait(false))
        {
            return UserErrors.EmailTaken;
        }

        var user = User.Register(email, hasher.Hash(command.Password));
        users.Add(user);

        return user.Id;
    }
}
