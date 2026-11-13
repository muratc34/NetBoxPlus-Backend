using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Outbox;
using Streaming.Application.Abstractions;
using Streaming.Domain.Access;
using Streaming.Domain.Videos;
using Streaming.Infrastructure.Media;
using Streaming.Infrastructure.Persistence;

namespace Streaming.Infrastructure;

public static class StreamingInfrastructureServiceCollectionExtensions
{
    /// <summary>Configuration key under <c>ConnectionStrings</c> for video storage.</summary>
    public const string VideosConnectionStringName = "Videos";

    /// <summary>
    /// Registers the database (from <c>ConnectionStrings:Database</c>), video storage (from
    /// <c>ConnectionStrings:Videos</c>) and RabbitMQ messaging (from the <c>Messaging</c> section).
    /// </summary>
    public static IServiceCollection AddStreamingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddStreamingCore(configuration);
        services.AddHostedService<ExpiredUploadCleanupService>();
        services.AddRabbitMqMessaging();
        return services;
    }

    /// <summary>
    /// What the transcoder process needs: the database, storage and ffmpeg. It does not talk to the
    /// broker; the API's outbox processor sends the events it writes.
    /// </summary>
    public static IServiceCollection AddStreamingTranscoder(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddStreamingCore(configuration);
        services.AddTranscoding(configuration);
        services.AddHostedService<TranscodeWorker>();

        // Whatever the configuration says: this process has no broker to send to.
        services.PostConfigure<OutboxOptions>(outbox => outbox.Enabled = false);
        return services;
    }

    /// <summary>The converter and its processor; the integration tests use it on its own.</summary>
    internal static IServiceCollection AddTranscoding(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TranscoderOptions>().Bind(configuration.GetSection(TranscoderOptions.SectionName));
        services.AddSingleton<FfmpegTranscoder>();
        services.AddScoped<TranscodeProcessor>();
        return services;
    }

    /// <summary>The database and repositories; the integration tests use it on its own.</summary>
    internal static IServiceCollection AddStreamingPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddPersistence<StreamingDbContext>(connectionString);
        services.AddScoped<IStreamingReadModel>(provider => provider.GetRequiredService<StreamingDbContext>());
        services.AddScoped<IVideoRepository, VideoRepository>();
        services.AddScoped<ISubscriptionCopyRepository, SubscriptionCopyRepository>();
        services.AddScoped<IProfileCopyRepository, ProfileCopyRepository>();
        services.AddScoped<IPlayableTitleRepository, PlayableTitleRepository>();
        services.AddScoped<ITranscodeQueue, EfTranscodeQueue>();
        return services;
    }

    private static void AddStreamingCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(StreamingDbContext.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{StreamingDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");
        var videos = configuration.GetConnectionString(VideosConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{VideosConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

        services.AddStreamingPersistence(connectionString);
        services.AddSingleton(new BlobServiceClient(videos));
        services.AddSingleton<VideoBlobStorage>();
        services.AddSingleton<IVideoStorage>(provider => provider.GetRequiredService<VideoBlobStorage>());
        services.AddScoped<ExpiredUploadCleaner>();
    }
}
