using Auth.Domain.Authentication;
using Auth.Domain.Users;

namespace Auth.Application.Abstractions;

public interface IJwtProvider
{
    AccessToken CreateToken(User user);
}
