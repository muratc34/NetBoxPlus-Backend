using System.Text.Json;
using Shared.Application.Messaging;

namespace Shared.Application.Tests;

public sealed class IntegrationEventsTests
{
    [Fact]
    public void MessageType_IsTheFullTypeName()
    {
        Assert.Equal("Shared.Application.Tests.ItemShipped", IntegrationEvents.MessageTypeOf(typeof(ItemShipped)));
    }

    [Fact]
    public void SerializerOptions_UseCamelCaseAndAreReadOnly()
    {
        var json = JsonSerializer.Serialize(new ItemShipped(Guid.Empty), IntegrationEvents.SerializerOptions);

        Assert.Contains("\"itemId\"", json, StringComparison.Ordinal);
        Assert.True(IntegrationEvents.SerializerOptions.IsReadOnly);
    }

    [Fact]
    public void Payload_RoundTrips()
    {
        var shipped = new ItemShipped(Guid.CreateVersion7());

        var json = JsonSerializer.Serialize(shipped, IntegrationEvents.SerializerOptions);

        Assert.Equal(shipped, JsonSerializer.Deserialize<ItemShipped>(json, IntegrationEvents.SerializerOptions));
    }
}
