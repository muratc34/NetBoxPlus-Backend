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

/// <summary>HMAC-SHA256 over the session, the file and the expiry, in the address's query string.</summary>
internal sealed class HmacPlaybackSigner(IOptions<PlaybackOptions> options) : IPlaybackSigner
{
    private readonly byte[] _key = options.Value.KeyBytes() ?? throw new InvalidOperationException($"{PlaybackOptions.SectionName}:SigningKey is not a valid key.");

    public string Sign(Guid sessionId, string path, DateTimeOffset expiresAt)
    {
        var expires = expiresAt.ToUnixTimeSeconds();
        return string.Create(CultureInfo.InvariantCulture, $"exp={expires}&sig={Base64Url.EncodeToString(Compute(sessionId, path, expires))}");
    }

    public bool IsValid(Guid sessionId, string path, long? expires, string? signature, DateTimeOffset now)
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

        return CryptographicOperations.FixedTimeEquals(given, Compute(sessionId, path, seconds));
    }

    private byte[] Compute(Guid sessionId, string path, long expires)
        => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{sessionId:N}\n{path}\n{expires}")));
}
