using Azure.Core;
using Azure.Storage.Blobs;

namespace NetBoxPlus.Testing;

/// <summary>
/// Talks to an Azurite test container so that a connection lost on the way does not fail the test.
/// Linked into every test project that uses an Azurite container.
/// </summary>
/// <remarks>
/// The same loss as in <see cref="ContainerConnections"/>: Docker Desktop's port forwarding accepts the
/// TCP connection and never hands it to the container, and the request waits for an answer that does
/// not come. The Azure SDK and <see cref="HttpClient"/> both wait 100 s by default. Here a request gets
/// <see cref="RequestTimeout"/> and is then tried again; the requests the tests make (creating
/// containers, uploading a whole blob, reading one) do the same when repeated.
/// </remarks>
internal static class StorageConnections
{
    /// <summary>How long one attempt of a request may take; the files the tests move are small.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private const int Attempts = 3;

    /// <summary>A client for the account that gives up a lost request early and tries it again.</summary>
    public static BlobServiceClient CreateClient(string connectionString)
        => new(connectionString, ClientOptions());

    public static BlobClientOptions ClientOptions()
    {
        var options = new BlobClientOptions();
        options.Retry.Mode = RetryMode.Fixed;
        options.Retry.MaxRetries = Attempts - 1;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
        options.Retry.NetworkTimeout = RequestTimeout;
        return options;
    }

    /// <summary>An HTTP client for storage addresses (SAS or public) with the same patience.</summary>
    public static HttpClient CreateHttpClient() => new(new RetryingHandler(new SocketsHttpHandler()));

    private sealed class RetryingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // A request message can be sent once: each attempt sends a copy, with the body read once.
            var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            for (var attempt = 1; ; attempt++)
            {
                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptTimeout.CancelAfter(RequestTimeout);
                try
                {
                    return await base.SendAsync(Copy(request, body), attemptTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < Attempts)
                {
                    TestContext.Current.TestOutputHelper?.WriteLine(
                        $"{request.Method} to storage got no answer in {RequestTimeout.TotalSeconds} s (attempt {attempt} of {Attempts}); trying again.");
                }
            }
        }

        private static HttpRequestMessage Copy(HttpRequestMessage request, byte[]? body)
        {
            var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
            foreach (var header in request.Headers)
            {
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (body is not null)
            {
                copy.Content = new ByteArrayContent(body);
                foreach (var header in request.Content!.Headers.Where(header => header.Key != "Content-Length"))
                {
                    copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return copy;
        }
    }
}
