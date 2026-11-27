namespace Auth.Contracts;

/// <summary>
/// Someone signed in to the account, with the password or a passkey: a new session (refresh token family) began.
/// Renewing a session or changing the password is not a sign-in.
/// </summary>
/// <param name="SessionId">The new sign-in, the same id the devices list shows.</param>
/// <param name="DeviceLabel">The browser and system it came from, such as "Chrome · Windows"; empty when unknown.</param>
/// <param name="Kind"><c>Customer</c> or <c>Admin</c>.</param>
/// <param name="Registration">The first session, opened by registering: the device is the account's own from the start.</param>
public sealed record SignedIn(
    Guid UserId,
    Guid SessionId,
    string? DeviceLabel,
    string Kind,
    DateTimeOffset SignedInAt,
    bool Registration,
    DateTimeOffset OccurredAt);
