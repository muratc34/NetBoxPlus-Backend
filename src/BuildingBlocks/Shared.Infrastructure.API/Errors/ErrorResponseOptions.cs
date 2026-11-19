namespace Shared.Infrastructure.API.Errors;

/// <summary>Settings of RFC 9457 error responses. Bound from the <c>ErrorResponses</c> section.</summary>
public sealed class ErrorResponseOptions
{
    public const string SectionName = "ErrorResponses";

    /// <summary>
    /// Base of every problem <c>type</c> URI. The kebab-cased error code is appended, so
    /// <c>User.DuplicateEmail</c> becomes <c>{base}user-duplicate-email</c>. Must be absolute and end
    /// with a slash.
    /// </summary>
    public Uri TypeBaseUri { get; set; } = new("https://netboxplus.dev/errors/");
}
