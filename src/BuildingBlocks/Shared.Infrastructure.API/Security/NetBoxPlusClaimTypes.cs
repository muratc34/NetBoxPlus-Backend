namespace Shared.Infrastructure.API.Security;

/// <summary>
/// Claim names exactly as they appear in NetBoxPlus access tokens. Inbound claim mapping is
/// disabled, so these short JWT names are what the application sees.
/// </summary>
public static class NetBoxPlusClaimTypes
{
    /// <summary>The user id (GUID).</summary>
    public const string Subject = "sub";

    public const string Role = "role";

    /// <summary>One claim per granted permission, for example <c>catalog:movies:write</c>.</summary>
    public const string Permission = "permission";
}
