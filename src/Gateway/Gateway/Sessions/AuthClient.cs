using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gateway.Sessions;

/// <summary>The auth service's token pair, as its API returns it. A copy: the gateway references no service.</summary>
internal sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

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

    public Task<AuthResult> SignInAsync(string? email, string? password, SessionKind kind, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, kind == SessionKind.Admin ? "/sign-in/admin" : "/sign-in", new { email, password }, null, cancellationToken);

    public Task<AuthResult> RegisterAsync(string? email, string? password, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "/register", new { email, password }, null, cancellationToken);

    public Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "/refresh", new { refreshToken }, null, cancellationToken);

    public Task<AuthResult> SignOutAsync(string refreshToken, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "/sign-out", new { refreshToken }, null, cancellationToken);

    public Task<AuthResult> ChangePasswordAsync(string accessToken, string? currentPassword, string? newPassword, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "/me/password", new { currentPassword, newPassword }, accessToken, cancellationToken);

    private async Task<AuthResult> SendAsync(HttpMethod method, string path, object body, string? accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var pair = await response.Content.ReadFromJsonAsync<TokenPair>(JsonSerializerOptions.Web, cancellationToken).ConfigureAwait(false);
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
