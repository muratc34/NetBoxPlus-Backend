using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Streaming.Application.Abstractions;

namespace Streaming.Infrastructure.Playback;

public sealed class PlaybackOptions
{
    public const string SectionName = "Playback";

    /// <summary>At least 32 random bytes, Base64. A new key makes every address handed out invalid.</summary>
    public string? SigningKey { get; set; }

    internal byte[]? KeyBytes()
    {
        try
        {
            return SigningKey is null ? null : Convert.FromBase64String(SigningKey);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    internal bool IsValid => KeyBytes() is { Length: >= 32 };
}

/// <summary>
/// HMAC-SHA256 over the session (or, for a trailer, a <c>trailer</c> line and the video), the file and the expiry, in the
/// address's query string. The two inputs differ in their first line, so neither kind of signature passes for the other.
/// </summary>
internal sealed class HmacPlaybackSigner(IOptions<PlaybackOptions> options) : IPlaybackSigner
{
    private const string TrailerScope = "trailer";

    private readonly byte[] _key = options.Value.KeyBytes() ?? throw new InvalidOperationException($"{PlaybackOptions.SectionName}:SigningKey is not a valid key.");

    public string Sign(Guid sessionId, string path, DateTimeOffset expiresAt) => SignFor(SessionScope(sessionId), path, expiresAt);

    public bool IsValid(Guid sessionId, string path, long? expires, string? signature, DateTimeOffset now)
        => IsValidFor(SessionScope(sessionId), path, expires, signature, now);

    public string SignTrailer(Guid videoId, string path, DateTimeOffset expiresAt) => SignFor(TrailerScopeOf(videoId), path, expiresAt);

    public bool IsValidTrailer(Guid videoId, string path, long? expires, string? signature, DateTimeOffset now)
        => IsValidFor(TrailerScopeOf(videoId), path, expires, signature, now);

    private static string SessionScope(Guid sessionId) => sessionId.ToString("N");

    private static string TrailerScopeOf(Guid videoId) => string.Create(CultureInfo.InvariantCulture, $"{TrailerScope}\n{videoId:N}");

    private string SignFor(string scope, string path, DateTimeOffset expiresAt)
    {
        var expires = expiresAt.ToUnixTimeSeconds();
        return string.Create(CultureInfo.InvariantCulture, $"exp={expires}&sig={Base64Url.EncodeToString(Compute(scope, path, expires))}");
    }

    private bool IsValidFor(string scope, string path, long? expires, string? signature, DateTimeOffset now)
    {
        if (expires is not { } seconds || string.IsNullOrEmpty(signature) || seconds <= now.ToUnixTimeSeconds())
        {
            return false;
        }

        byte[] given;
        try
        {
            given = Base64Url.DecodeFromChars(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(given, Compute(scope, path, seconds));
    }

    // A session's scope is its id as 32 hex digits, so a session's input is unchanged and its old addresses stay valid.
    private byte[] Compute(string scope, string path, long expires)
        => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{scope}\n{path}\n{expires}")));
}
