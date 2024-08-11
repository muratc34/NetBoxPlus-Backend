using Auth.Application.Abstractions;
using Auth.Application.Core.Extensions;
using Microsoft.AspNetCore.Http;

namespace Auth.Infrastructure;

public sealed class UserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    public Guid UserId =>
        httpContextAccessor
            .HttpContext?
            .User?
            .GetUserId() ??
        throw new ApplicationException("User context is unavailable");

    public bool IsAuthenticated =>
        httpContextAccessor
            .HttpContext?
            .User?
            .Identity?
            .IsAuthenticated ??
        throw new ApplicationException("User context is unavailable");
}
