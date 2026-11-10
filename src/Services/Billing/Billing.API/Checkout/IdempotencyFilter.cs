using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Billing.Application.Abstractions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Shared.Application;
using Shared.Domain;
using Shared.Infrastructure.API.Errors;

namespace Billing.API.Checkout;

/// <summary>Marks the body of a request whose content decides whether a reused Idempotency-Key matches.</summary>
internal interface IIdempotentBody;

/// <summary>
/// Requires an Idempotency-Key and makes the endpoint run once per key and user: the same request
/// again gets the first response back; another request with the same key is refused. Only
/// successful responses are kept; after a failure the key can be used again.
/// </summary>
internal sealed class IdempotencyFilter(IIdempotencyStore store, ICurrentUser currentUser, IOptions<JsonOptions> json) : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotent-Replayed";
    private const int MaxKeyLength = 255;

    public static readonly Error KeyRequired = Error.Validation("Idempotency.KeyRequired", $"Send an {HeaderName} header of 1 to {MaxKeyLength} printable characters, new for each new request.");

    public static readonly Error KeyReused = Error.Unprocessable("Idempotency.KeyReused", $"This {HeaderName} was already used for a different request.");

    public static readonly Error InProgress = Error.Conflict("Idempotency.InProgress", $"A request with this {HeaderName} is still being processed. Try again shortly.");

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString();
        if (key.Length is 0 or > MaxKeyLength || !key.All(character => character is >= ' ' and <= '~'))
        {
            return KeyRequired.ToProblem();
        }

        // RequireAuthorization runs first, so there is always a user here.
        var userId = currentUser.Id!.Value;
        var start = await store.StartAsync(userId, key, Hash(context), http.RequestAborted).ConfigureAwait(false);

        switch (start.Outcome)
        {
            case IdempotencyOutcome.Replay:
                http.Response.Headers[ReplayedHeaderName] = "true";
                return TypedResults.Content(start.ResponseBody, "application/json", Encoding.UTF8, start.StatusCode);
            case IdempotencyOutcome.KeyReused:
                return KeyReused.ToProblem();
            case IdempotencyOutcome.InProgress:
                return InProgress.ToProblem();
        }

        object? result;
        try
        {
            result = await next(context).ConfigureAwait(false);
        }
        catch
        {
            await store.ReleaseAsync(userId, key, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 } success and IValueHttpResult value)
        {
            var body = JsonSerializer.Serialize(value.Value, json.Value.SerializerOptions);
            await store.CompleteAsync(userId, key, success.StatusCode!.Value, body, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            await store.ReleaseAsync(userId, key, CancellationToken.None).ConfigureAwait(false);
        }

        return result;
    }

    // Method, path and the body: the key header itself and the services are not part of the request.
    private string Hash(EndpointFilterInvocationContext context)
    {
        var request = context.HttpContext.Request;
        var bodies = context.Arguments.OfType<IIdempotentBody>().Select(body => JsonSerializer.Serialize(body, body.GetType(), json.Value.SerializerOptions));
        var text = $"{request.Method} {request.Path}\n{string.Join('\n', bodies)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
