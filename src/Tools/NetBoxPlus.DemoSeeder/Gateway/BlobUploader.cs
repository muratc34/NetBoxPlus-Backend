using System.Net.Http.Headers;

namespace NetBoxPlus.DemoSeeder.Gateway;

/// <summary>Puts a file at a signed upload address, as a browser does with the address a service hands out.</summary>
internal sealed class BlobUploader(HttpClient http, StorageAddresses addresses)
{
    public async Task UploadAsync(Uri uploadAddress, string file, string contentType, CancellationToken cancellationToken)
    {
        var stream = File.OpenRead(file);
        await using (stream.ConfigureAwait(false))
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, addresses.ToReachable(uploadAddress))
            {
                Content = new StreamContent(stream),
            };
            request.Headers.Add("x-ms-blob-type", "BlockBlob");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Uploading {Path.GetFileName(file)} answered {(int)response.StatusCode}.");
            }
        }
    }
}
