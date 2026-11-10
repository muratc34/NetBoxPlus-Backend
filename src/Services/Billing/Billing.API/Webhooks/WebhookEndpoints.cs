using System.Security.Cryptography;
using System.Text;
using Billing.Application.Webhooks;
using Billing.Application.Webhooks.SyncPaymentObject;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;
using Shared.Infrastructure.API.Errors;

namespace Billing.API.Webhooks;

/// <summary>
/// Stripe's calls. Not part of the client API: anonymous, proven by the signature over the body.
/// Each event is handled once (inbox). The answer tells Stripe whether to send it again: 2xx
/// done or nothing to do, 400 never valid, anything else try later.
/// </summary>
internal static partial class WebhookEndpoints
{
    /// <summary>Inbox key of Stripe events; renaming it would handle every past event again.</summary>
    public const string InboxConsumer = "Stripe.Webhook";

    private const int MaxPayloadBytes = 512 * 1024;

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/webhooks/stripe", ReceiveAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return endpoints;
    }

    /// <summary>Stripe's event ids are not Guids; the inbox gets the same Guid for the same event every time.</summary>
    public static Guid MessageIdOf(string eventId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("stripe:" + eventId));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static async Task<IResult> ReceiveAsync(
        HttpContext context,
        [FromServices] IPaymentWebhook webhook,
        [FromServices] IInbox inbox,
        [FromServices] IDispatcher dispatcher,
        [FromServices] ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > MaxPayloadBytes)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        string payload;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
        }

        var read = webhook.Read(payload, context.Request.Headers["Stripe-Signature"].ToString());
        if (read.IsFailure)
        {
            return read.Error.ToProblem();
        }

        if (read.Value is not { } paymentEvent)
        {
            return TypedResults.Ok();
        }

        var logger = loggers.CreateLogger(typeof(WebhookEndpoints));
        try
        {
            await inbox.ProcessOnceAsync(
                MessageIdOf(paymentEvent.Id),
                InboxConsumer,
                async token =>
                {
                    var result = await dispatcher.Send(new SyncPaymentObjectCommand(paymentEvent.Kind, paymentEvent.ObjectId), token);

                    // Kept as handled only when sending it again cannot help; otherwise Stripe retries.
                    if (result.IsFailure && IsWorthRetrying(result.Error))
                    {
                        throw new RetryLaterException(result.Error);
                    }

                    if (result.IsFailure)
                    {
                        LogDropped(logger, paymentEvent.Id, paymentEvent.Type, result.Error.Code);
                    }

                    return result;
                },
                cancellationToken);
        }
        catch (RetryLaterException exception)
        {
            LogRetry(logger, paymentEvent.Id, paymentEvent.Type, exception.Error.Code);
            return exception.Error.ToProblem();
        }

        return TypedResults.Ok();
    }

    private static bool IsWorthRetrying(Error error)
        => error.Type is ErrorType.Unavailable or ErrorType.Failure or ErrorType.Conflict or ErrorType.UpstreamFailure;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stripe event {EventId} ({EventType}) failed with {ErrorCode}; Stripe will send it again")]
    private static partial void LogRetry(ILogger logger, string eventId, string eventType, string errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stripe event {EventId} ({EventType}) cannot be applied ({ErrorCode}) and is not retried")]
    private static partial void LogDropped(ILogger logger, string eventId, string eventType, string errorCode);

    private sealed class RetryLaterException(Error error) : Exception(error.Code)
    {
        public Error Error { get; } = error;
    }
}
