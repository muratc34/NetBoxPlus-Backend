using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NetBoxPlus.DemoSeeder.Gateway;

namespace NetBoxPlus.DemoSeeder.Tests;

public sealed class GatewayClientTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SignIn_KeepsTheSessionCookie_AndSendsItWithTheCsrfToken()
    {
        var handler = new ScriptedHandler(
            SignedIn(),
            Json(HttpStatusCode.OK, """{"id":"0195f1c2-0000-7000-8000-000000000001"}"""));
        var client = Client(handler);

        await client.SignInAsync("admin@netboxplus.local", "password", Cancellation);
        var created = await client.PostAsync<Created>("/admin/catalog/genres", new { name = "x" }, Cancellation);

        Assert.Equal(Guid.Parse("0195f1c2-0000-7000-8000-000000000001"), created.Id);
        var sent = handler.Requests[1];
        Assert.Equal("__Host-nbp-admin=session-value", sent.Headers.GetValues("Cookie").Single());
        Assert.Equal("csrf-value", sent.Headers.GetValues("X-CSRF-Token").Single());
    }

    [Fact]
    public async Task ARequestBeforeSigningIn_IsRefused()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => Client(new ScriptedHandler()).GetAsync<Created>("/admin/catalog/genres", Cancellation));

    [Fact]
    public async Task AThrottledRequest_IsSentAgainAfterTheWait()
    {
        var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        var handler = new ScriptedHandler(SignedIn(), throttled, Json(HttpStatusCode.OK, """{"id":"0195f1c2-0000-7000-8000-000000000002"}"""));
        var client = Client(handler);
        await client.SignInAsync("admin@netboxplus.local", "password", Cancellation);

        await client.PostAsync<Created>("/admin/catalog/genres", new { name = "x" }, Cancellation);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(handler.Requests[1].RequestUri, handler.Requests[2].RequestUri);
        Assert.Equal("""{"name":"x"}""", handler.Bodies[2]);
    }

    [Fact]
    public async Task AFailure_CarriesTheProblemCode()
    {
        var handler = new ScriptedHandler(
            SignedIn(),
            Json(HttpStatusCode.Conflict, """{"status":409,"code":"Content.VideoRequired","detail":"A video is required."}""", "application/problem+json"));
        var client = Client(handler);
        await client.SignInAsync("admin@netboxplus.local", "password", Cancellation);

        var failure = await Assert.ThrowsAsync<GatewayException>(() => client.PostAsync("/admin/catalog/movies/1/publish", body: null, Cancellation));

        Assert.Equal(HttpStatusCode.Conflict, failure.Status);
        Assert.Equal("Content.VideoRequired", failure.Code);
    }

    [Fact]
    public async Task ASignInWithoutASessionCookie_Fails()
    {
        var handler = new ScriptedHandler(Json(HttpStatusCode.OK, """{"csrfToken":"csrf-value"}"""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).SignInAsync("admin@netboxplus.local", "password", Cancellation));
    }

    [Theory]
    [InlineData(null, 1000)]
    [InlineData(3, 3000)]
    [InlineData(0, 100)]
    [InlineData(600, 30000)]
    public void TheWait_FollowsRetryAfter_WithinLimits(int? seconds, int expectedMilliseconds)
    {
        var header = seconds is { } value ? new RetryConditionHeaderValue(TimeSpan.FromSeconds(value)) : null;
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), GatewayClient.RetryDelay(header));
    }

    private static GatewayClient Client(ScriptedHandler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("http://gateway.test") });

    private static HttpResponseMessage SignedIn()
    {
        var response = Json(HttpStatusCode.OK, """{"csrfToken":"csrf-value"}""");
        response.Headers.Add("Set-Cookie", "__Host-nbp-session=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT");
        response.Headers.Add("Set-Cookie", "__Host-nbp-admin=session-value; path=/; secure; httponly; samesite=strict");
        return response;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string mediaType = "application/json")
        => new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private sealed class ScriptedHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responses.Dequeue();
        }
    }
}
