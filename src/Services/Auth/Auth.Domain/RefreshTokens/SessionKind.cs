namespace Auth.Domain.RefreshTokens;

/// <summary>Which application a token family was issued for; each has its own lifetimes.</summary>
public enum SessionKind
{
    /// <summary>The customer-facing application.</summary>
    Customer = 1,

    /// <summary>The admin panel: short-lived, so a forgotten admin session does not stay open.</summary>
    Admin = 2,
}
