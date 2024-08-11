using Shared.Application.Abstractions;
using Shared.Domain.Result;

namespace Auth.Application.Authentication.ChangePassword;

public sealed class ChangePasswordCommand : ICommand<Result>
{
    public ChangePasswordCommand(Guid userId, string password)
    {
        UserId = userId;
        Password = password;
    }
    public Guid UserId { get; }
    public string Password { get; }
}
