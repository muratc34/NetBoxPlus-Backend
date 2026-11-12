using Streaming.Domain.Videos;

namespace Streaming.Application.Abstractions;

/// <summary>Where uploaded video files arrive.</summary>
public interface IVideoStorage
{
    /// <summary>A write-only address for the upload, good until <paramref name="expiresAt"/>.</summary>
    Uri CreateUploadAddress(Guid videoId, DateTimeOffset expiresAt);

    /// <summary>The size of the uploaded file, or <see langword="null"/> when nothing has arrived.</summary>
    Task<long?> GetUploadSizeAsync(Guid videoId, CancellationToken cancellationToken);
}

/// <summary>Videos waiting for conversion; a line is added with the change that queued the video.</summary>
public interface ITranscodeQueue
{
    void Enqueue(Guid videoId, DateTimeOffset now);

    /// <summary>Takes the video out of line, with the change that finished it.</summary>
    void Remove(Guid videoId);
}

/// <summary>Untracked queryable views for query handlers.</summary>
public interface IStreamingReadModel
{
    IQueryable<Video> Videos { get; }
}
