using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gateway.Sessions;

/// <summary>The auth service's token pair, as its API returns it. A copy: the gateway references no service.</summary>
/// <param name="SessionId">The sign-in (refresh token family) the pair belongs to; the same for every renewal.</param>
internal sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, Guid? SessionId = null);

/// <summary>An answer of the auth service passed on as it is: its status and JSON body. <see cref="IsUnavailable"/> as in <see cref="AuthResult"/>.</summary>
internal sealed record AuthJson(int StatusCode, string? Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public bool IsUnavailable => StatusCode is 0 or >= 500;
}

/// <summary>
/// The answer of the auth service: a token pair, or its problem response, passed on to the browser
/// unchanged (it never contains tokens). <see cref="IsUnavailable"/> when the service could not be
/// reached or failed.
/// </summary>
internal sealed record AuthResult(TokenPair? Pair, int StatusCode, string? ProblemBody)
{
    public bool IsSuccess => Pair is not null || StatusCode is >= 200 and < 300;

    public bool IsUnavailable => StatusCode is 0 or >= 500;

    public static AuthResult Unavailable { get; } = new(null, 0, null);
}

/// <summary>
/// Calls the auth service for the gateway's own session endpoints. Nothing is retried: a refresh
/// token works once, and a second try would look like reuse and end the whole session family.
/// </summary>
internal sealed partial class AuthClient(IHttpClientFactory httpClientFactory, ILogger<AuthClient> logger)
{
    /// <summary>The named HTTP client, with the auth service's address and a timeout.</summary>
    public const string HttpClientName = "auth";

    /// <summary>More than any real browser sends; the auth service keeps only a short name derived from it.</summary>
    private const int MaxUserAgentLength = 512;

    /// <param name="userAgent">The browser's, passed on: the auth service names the session's device after it.</param>
    public Task<AuthResult> SignInAsync(string? email, string? password, SessionKind kind, string? userAgent, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, kind == SessionKind.Admin ? "/sign-in/admin" : "/sign-in", new { email, password }, null, cancellationToken, userAgent);

    public Task<AuthResult> RegisterAsync(string? email, string? password, string? userAgent, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "/register", new { email, password }, null, cancellationToken, userAgent);

    public Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "/refresh", new { refreshToken }, null, cancellationToken);

    public Task<AuthResult> SignOutAsync(string refreshToken, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "/sign-out", new { refreshToken }, null, cancellationToken);

    public Task<AuthResult> ChangePasswordAsync(string accessToken, string? currentPassword, string? newPassword, string? userAgent, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "/me/password", new { currentPassword, newPassword }, accessToken, cancellationToken, userAgent);

    public Task<AuthJson> StartPasskeySignInAsync(CancellationToken cancellationToken)
        => SendForJsonAsync(HttpMethod.Post, "/sign-in/passkey/options", null, cancellationToken, new { });

    /// <param name="answer">The browser's challenge id and credential, passed on as they came.</param>
    public Task<AuthResult> PasskeySignInAsync(object answer, SessionKind kind, string? userAgent, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, kind == SessionKind.Admin ? "/sign-in/passkey/admin" : "/sign-in/passkey", answer, null, cancellationToken, userAgent);

    /// <summary>The account's passkeys: listing, the options for adding one, adding, renaming and removing; answers passed on.</summary>
    public Task<AuthJson> PasskeysAsync(HttpMethod method, string path, string accessToken, object? body, CancellationToken cancellationToken)
        => SendForJsonAsync(method, "/me/passkeys" + path, accessToken, cancellationToken, body);

    public Task<AuthJson> UnlockProfileAsync(string accessToken, Guid profileId, string? pin, CancellationToken cancellationToken)
        => SendForJsonAsync(HttpMethod.Post, $"/profiles/{profileId}/unlock", accessToken, cancellationToken, new { pin });

    public Task<AuthJson> GetSessionsAsync(string accessToken, CancellationToken cancellationToken)
        => SendForJsonAsync(HttpMethod.Get, "/me/sessions", accessToken, cancellationToken);

    public Task<AuthJson> EndSessionAsync(string accessToken, Guid sessionId, CancellationToken cancellationToken)
        => SendForJsonAsync(HttpMethod.Delete, $"/me/sessions/{sessionId}", accessToken, cancellationToken);

    public Task<AuthJson> EndOtherSessionsAsync(string accessToken, CancellationToken cancellationToken)
        => SendForJsonAsync(HttpMethod.Delete, "/me/sessions", accessToken, cancellationToken);

    public Task<AuthResult> RequestEmailChangeAsync(string accessToken, string? password, string? newEmail, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "/me/email", new { password, newEmail }, accessToken, cancellationToken);

    public Task<AuthResult> DeleteAccountAsync(string accessToken, string? password, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "/me/deletion", new { password }, accessToken, cancellationToken);

    public Task<AuthResult> RequestPasswordResetAsync(string? email, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "/password-reset", new { email }, null, cancellationToken);

    public Task<AuthResult> ResetPasswordAsync(string? token, string? newPassword, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "/password-reset/confirm", new { token, newPassword }, null, cancellationToken);

    /// <summary>A call whose answer the gateway passes on: its status and body, whatever they are.</summary>
    private async Task<AuthJson> SendForJsonAsync(HttpMethod method, string path, string? accessToken, CancellationToken cancellationToken, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode >= 500)
            {
                LogFailed(logger, path, (int)response.StatusCode);
                return new AuthJson(0, null);
            }

            var text = response.Content.Headers.ContentLength == 0
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new AuthJson((int)response.StatusCode, text);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(logger, path, exception);
            return new AuthJson(0, null);
        }
    }

    private async Task<AuthResult> SendAsync(HttpMethod method, string path, object body, string? accessToken, CancellationToken cancellationToken, string? userAgent = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        // As the browser sent it; a header the parser refuses is left out rather than failing the sign-in.
        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent.Length <= MaxUserAgentLength ? userAgent : userAgent[..MaxUserAgentLength]);
        }

        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken).ConfigureAwait(false);

            // A registration answers 201 with the same fields as a sign-in, and the account's id.
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
            {
                var pair = await response.Content.ReadFromJsonAsync<TokenPair>(JsonSerializerOptions.Web, cancellationToken).ConfigureAwait(false);
                if (pair is not { AccessToken.Length: > 0, RefreshToken.Length: > 0 })
                {
                    // A success without tokens is the service's fault, not something to start a session with.
                    LogFailed(logger, path, (int)response.StatusCode);
                    return AuthResult.Unavailable;
                }

                return new AuthResult(pair, (int)response.StatusCode, null);
            }

            if ((int)response.StatusCode >= 500)
            {
                LogFailed(logger, path, (int)response.StatusCode);
                return AuthResult.Unavailable;
            }

            var problem = response.Content.Headers.ContentLength == 0
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new AuthResult(null, (int)response.StatusCode, problem);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(logger, path, exception);
            return AuthResult.Unavailable;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The auth service failed {Path} with {StatusCode}")]
    private static partial void LogFailed(ILogger logger, string path, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The auth service could not be reached for {Path}")]
    private static partial void LogUnreachable(ILogger logger, string path, Exception exception);
}
