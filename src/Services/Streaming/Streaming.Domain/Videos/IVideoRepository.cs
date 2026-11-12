namespace Streaming.Domain.Videos;

public interface IVideoRepository
{
    Task<Video?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(Video video);
}
