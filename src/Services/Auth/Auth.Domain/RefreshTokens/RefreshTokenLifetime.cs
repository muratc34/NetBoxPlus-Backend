namespace Auth.Domain.RefreshTokens;

/// <summary>
/// How long a single token and a whole family live. Each refresh extends the session by
/// <see cref="Token"/>, never past <see cref="Family"/> from the sign-in.
/// </summary>
public sealed record RefreshTokenLifetime(TimeSpan Token, TimeSpan Family)
{
    /// <summary>Seven days idle, thirty days at most; then the customer signs in again.</summary>
    public static readonly RefreshTokenLifetime Customer = new(TimeSpan.FromDays(7), TimeSpan.FromDays(30));

    /// <summary>One working day, not extended: an admin signs in again every day.</summary>
    public static readonly RefreshTokenLifetime Admin = new(TimeSpan.FromHours(8), TimeSpan.FromHours(8));

    public static RefreshTokenLifetime For(SessionKind kind) => kind switch
    {
        SessionKind.Customer => Customer,
        SessionKind.Admin => Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown session kind."),
    };
}
