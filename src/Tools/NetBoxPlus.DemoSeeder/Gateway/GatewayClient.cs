using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetBoxPlus.DemoSeeder.Gateway;

/// <summary>
/// The gateway's admin API as the admin panel uses it: a session cookie from the admin sign-in,
/// and the CSRF token on every request that changes something.
/// </summary>
internal sealed class GatewayClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private const string SessionCookie = "__Host-nbp-admin";

    private string? _cookie;
    private string? _csrfToken;

    public async Task SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/admin/session/sign-in", new { email, password }, Json, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, HttpMethod.Post, "/admin/session/sign-in", cancellationToken).ConfigureAwait(false);

        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(value => value.Split(';')[0]).FirstOrDefault(value => value.StartsWith(SessionCookie + "=", StringComparison.Ordinal))
            : null;
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(Json, cancellationToken).ConfigureAwait(false);
        _cookie = cookie ?? throw new InvalidOperationException("The admin sign-in returned no session cookie.");
        _csrfToken = session?.CsrfToken ?? throw new InvalidOperationException("The admin sign-in returned no CSRF token.");
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, body: null, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> PostAsync<T>(string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task PostAsync(string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken).ConfigureAwait(false);
    }

    public async Task PutAsync(string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Put, path, body, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        if (_cookie is null || _csrfToken is null)
        {
            throw new InvalidOperationException("Sign in first.");
        }

        var response = await SendThrottledAsync(method, path, body, cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSuccessAsync(response, method, path, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The gateway limits admin requests per window and answers 429 with Retry-After before the
    /// request reaches a service, so sending it again after the wait is safe, writes included.
    /// </summary>
    private async Task<HttpResponseMessage> SendThrottledAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("Cookie", _cookie);
            request.Headers.Add("X-CSRF-Token", _csrfToken);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            }

            var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == MaxThrottledAttempts)
            {
                return response;
            }

            var wait = RetryDelay(response.Headers.RetryAfter);
            response.Dispose();
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private const int MaxThrottledAttempts = 20;

    internal static TimeSpan RetryDelay(RetryConditionHeaderValue? retryAfter)
    {
        // The gateway sends seconds; anything else waits a second.
        var delay = retryAfter?.Delta ?? TimeSpan.FromSeconds(1);
        return delay < TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        => await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The response to {response.RequestMessage?.RequestUri} had no body.");

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, HttpMethod method, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? code = null;
        string? detail = null;
        if (response.Content.Headers.ContentType is MediaTypeHeaderValue { MediaType: "application/problem+json" or "application/json" })
        {
            try
            {
                var problem = await response.Content.ReadFromJsonAsync<Problem>(Json, cancellationToken).ConfigureAwait(false);
                code = problem?.Code;
                detail = problem?.Detail;
            }
            catch (JsonException)
            {
                // A body that is not a problem; the status says enough.
            }
        }

        throw new GatewayException(method, path, response.StatusCode, code, detail);
    }

    private sealed record SessionResponse(string? CsrfToken);

    private sealed record Problem(string? Code, string? Detail);
}

internal sealed class GatewayException(HttpMethod method, string path, HttpStatusCode status, string? code, string? detail)
    : Exception($"{method} {path} answered {(int)status} {code ?? status.ToString()}{(detail is null ? "" : ": " + detail)}")
{
    public HttpStatusCode Status { get; } = status;

    /// <summary>The error code from the problem details, such as <c>Content.VideoRequired</c>.</summary>
    public string? Code { get; } = code;
}
