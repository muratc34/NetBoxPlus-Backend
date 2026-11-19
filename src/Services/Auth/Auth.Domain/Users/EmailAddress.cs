using System.Net.Mail;
using Shared.Domain;

namespace Auth.Domain.Users;

/// <summary>
/// A user's e-mail address, trimmed and in lower case, so that the same address always compares
/// equal and is unique in the database.
/// </summary>
public sealed record EmailAddress
{
    /// <summary>The longest address SMTP can carry (RFC 5321).</summary>
    public const int MaxLength = 254;

    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static Result<EmailAddress> Create(string? value)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return UserErrors.EmailRequired;
        }

        if (trimmed.Length > MaxLength)
        {
            return UserErrors.EmailTooLong;
        }

        // A bare address only: MailAddress also accepts forms such as "Name <user@example.com>".
        if (!MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed)
        {
            return UserErrors.EmailInvalid;
        }

        return new EmailAddress(trimmed.ToLowerInvariant());
    }

    public override string ToString() => Value;
}
