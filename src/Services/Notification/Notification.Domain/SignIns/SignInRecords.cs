using System.Buffers.Text;
using System.Security.Cryptography;
using Shared.Domain;

namespace Notification.Domain.SignIns;

/// <summary>
/// A browser and system a user signed in from, such as "Chrome · Windows", and when it was last seen. A sign-in from a
/// device not seen for <see cref="RememberedFor"/> is new, and the user is told. The label is what the account service
/// made of the browser's User-Agent; no address or other detail is kept.
/// </summary>
public sealed class KnownDevice
{
    public const int LabelMaxLength = 100;

    public static readonly TimeSpan RememberedFor = TimeSpan.FromDays(30);

    private KnownDevice(Guid userId, string label, DateTimeOffset lastSeenAt)
    {
        UserId = userId;
        Label = label;
        LastSeenAt = lastSeenAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private KnownDevice()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>Empty when the account service could not tell the device: all such sign-ins count as one device.</summary>
    public string Label { get; private set; } = string.Empty;

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>The label as it is stored: trimmed, at most <see cref="LabelMaxLength"/> characters, empty when unknown.</summary>
    public static string LabelOf(string? deviceLabel)
    {
        var trimmed = deviceLabel?.Trim() ?? string.Empty;
        return trimmed.Length > LabelMaxLength ? trimmed[..LabelMaxLength] : trimmed;
    }

    public static KnownDevice Create(Guid userId, string label, DateTimeOffset seenAt) => new(userId, LabelOf(label), seenAt);

    /// <summary>Whether a sign-in at <paramref name="signedInAt"/> comes from a device seen recently enough.</summary>
    public bool IsKnownAt(DateTimeOffset signedInAt) => LastSeenAt > signedInAt - RememberedFor;

    /// <summary>Seen again; an older sign-in arriving late does not move the time back.</summary>
    public void Seen(DateTimeOffset at)
    {
        if (at > LastSeenAt)
        {
            LastSeenAt = at;
        }
    }
}

/// <summary>What the user wants to be told; without a row, everything is on.</summary>
public sealed class NotificationPreference
{
    private NotificationPreference(Guid userId)
    {
        UserId = userId;
        SignInAlerts = true;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private NotificationPreference()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>An e-mail for a sign-in from a new device.</summary>
    public bool SignInAlerts { get; private set; }

    public DateTimeOffset? ChangedAt { get; private set; }

    public static NotificationPreference Create(Guid userId) => new(userId);

    public void Change(bool signInAlerts, DateTimeOffset now)
    {
        if (SignInAlerts == signInAlerts)
        {
            return;
        }

        SignInAlerts = signInAlerts;
        ChangedAt = now;
    }
}

/// <summary>
/// The address in an alert that turns the alerts off in one click (RFC 8058), without signing in. Only the SHA-256 hash
/// of its token is kept; it works for <see cref="Lifetime"/>, as often as it is used (mail programs may ask twice).
/// </summary>
public sealed class UnsubscribeLink
{
    public const int TokenBytes = 32;

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    private UnsubscribeLink(Guid id, Guid userId, byte[] tokenHash, DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private UnsubscribeLink()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset ExpiresAt { get; private set; }

    public static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    /// <summary>A new link and the token that goes into the e-mail; the token itself is not kept.</summary>
    public static (UnsubscribeLink Link, string Token) Issue(Guid userId, DateTimeOffset now)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        return (new UnsubscribeLink(Guid.CreateVersion7(), userId, Hash(token), now + Lifetime), token);
    }

    public bool IsValidAt(DateTimeOffset now) => ExpiresAt > now;
}

public static class SignInAlertErrors
{
    public static readonly Error UnsubscribeTokenInvalid = Error.Validation("Unsubscribe.TokenInvalid", "The link is not valid or has expired; turn the alerts off from your account.");
}

public interface ISignInRecordRepository
{
    Task<KnownDevice?> GetDeviceAsync(Guid userId, string label, CancellationToken cancellationToken);

    void AddDevice(KnownDevice device);

    Task<NotificationPreference?> GetPreferenceAsync(Guid userId, CancellationToken cancellationToken);

    void AddPreference(NotificationPreference preference);

    Task<UnsubscribeLink?> GetLinkAsync(byte[] tokenHash, CancellationToken cancellationToken);

    void AddLink(UnsubscribeLink link);

    /// <summary>The user's devices, preference and links, forgotten with the account; saved with the command.</summary>
    Task RemoveForUserAsync(Guid userId, CancellationToken cancellationToken);
}
