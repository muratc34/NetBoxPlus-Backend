using Auth.Domain.Authentication;
using Shared.Application.Abstractions;
using Shared.Domain.Result;

namespace Auth.Application.Authentication.Login;

public sealed class LoginCommand : ICommand<Result<AccessToken>>
{
    public LoginCommand(string email, string password)
    {
        Email = email;
        Password = password;
    }
    public string Email { get; }
    public string Password { get; }
}
