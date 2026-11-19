using System.Text.Json;

namespace Shared.Application.Messaging;

/// <summary>Naming and serialization rules every service applies to integration events.</summary>
public static class IntegrationEvents
{
    /// <summary>Serializer settings shared by the publishing and the consuming side.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    /// <summary>
    /// The message type of an event: its full type name. Contract types are never renamed or moved,
    /// because consumers find them by this name.
    /// </summary>
    public static string MessageTypeOf(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return eventType.FullName
            ?? throw new ArgumentException($"'{eventType}' has no full name and cannot be an integration event.", nameof(eventType));
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
