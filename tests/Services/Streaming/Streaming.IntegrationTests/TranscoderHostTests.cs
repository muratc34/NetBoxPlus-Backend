using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Infrastructure.Persistence.Outbox;
using Streaming.Infrastructure;

namespace Streaming.IntegrationTests;

public sealed class TranscoderHostTests
{
    [Fact]
    public void TheTranscoder_NeverSendsTheOutbox_WhateverItsSettingsSay()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Host=nowhere",
            ["ConnectionStrings:Videos"] = "UseDevelopmentStorage=true",
            ["Outbox:Enabled"] = "true",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddStreamingTranscoder(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<IOptions<OutboxOptions>>().Value.Enabled);
    }
}
