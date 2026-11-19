using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Shared.Application;

namespace Shared.Infrastructure.API.Security;

/// <summary>The caller of the current HTTP request, read from its validated access token.</summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? Id => IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(NetBoxPlusClaimTypes.Subject), out var id)
        ? id
        : null;

    public bool HasPermission(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return IsAuthenticated && Principal!.HasClaim(NetBoxPlusClaimTypes.Permission, permission);
    }
}
