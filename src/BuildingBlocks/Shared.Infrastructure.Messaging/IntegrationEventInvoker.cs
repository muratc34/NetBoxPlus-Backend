using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Delivers one message to every handler of its event type. Each handler runs in its own scope
/// and through the inbox, so a retry after one handler failed skips the handlers that succeeded.
/// </summary>
internal abstract partial class IntegrationEventInvoker
{
    public abstract Task InvokeAsync(
        IServiceScopeFactory scopeFactory,
        Guid messageId,
        ReadOnlyMemory<byte> payload,
        ILogger logger,
        CancellationToken cancellationToken);

    public static IntegrationEventInvoker For(Type eventType)
        => (IntegrationEventInvoker)Activator.CreateInstance(typeof(IntegrationEventInvoker<>).MakeGenericType(eventType))!;

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Handler} rejected message {MessageId} with {ErrorCode}; it is not retried")]
    protected static partial void LogRejected(ILogger logger, string handler, Guid messageId, string errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Handler} already processed message {MessageId}")]
    protected static partial void LogDuplicate(ILogger logger, string handler, Guid messageId);
}

internal sealed class IntegrationEventInvoker<TEvent> : IntegrationEventInvoker
    where TEvent : class
{
    public override async Task InvokeAsync(
        IServiceScopeFactory scopeFactory,
        Guid messageId,
        ReadOnlyMemory<byte> payload,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var integrationEvent = JsonSerializer.Deserialize<TEvent>(payload.Span, IntegrationEvents.SerializerOptions)
            ?? throw new JsonException($"Message {messageId} has an empty payload.");

        int handlerCount;
        await using (var probe = scopeFactory.CreateAsyncScope())
        {
            handlerCount = probe.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>().Count();
        }

        for (var index = 0; index < handlerCount; index++)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>().ElementAt(index);
            var handlerName = handler.GetType().FullName!;
            var inbox = scope.ServiceProvider.GetRequiredService<IInbox>();

            var processed = await inbox.ProcessOnceAsync(
                messageId,
                handlerName,
                async token =>
                {
                    var result = await handler.Handle(integrationEvent, token).ConfigureAwait(false);
                    if (result.IsFailure)
                    {
                        LogRejected(logger, handlerName, messageId, result.Error.Code);
                    }

                    return result;
                },
                cancellationToken).ConfigureAwait(false);

            if (!processed)
            {
                LogDuplicate(logger, handlerName, messageId);
            }
        }
    }
}
