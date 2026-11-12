using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Application.Validation;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Domain.Videos;

namespace Streaming.Application.Videos.GetVideos;

/// <summary>Videos for the admin panel, newest first.</summary>
public sealed record GetVideosQuery(Guid? ContentId, VideoStatus? Status, int PageNumber, int PageSize) : IQuery<Page<VideoSummary>>;

public sealed record VideoSummary(Guid Id, ContentKind ContentKind, Guid ContentId, VideoStatus Status, double? DurationSeconds, DateTimeOffset CreatedAt);

/// <summary>A video with what it was converted to, or why it was not.</summary>
public sealed record VideoDetails(
    Guid Id,
    ContentKind ContentKind,
    Guid ContentId,
    VideoStatus Status,
    long? SizeBytes,
    double? DurationSeconds,
    IReadOnlyList<Rendition> Renditions,
    string? FailureReason,
    DateTimeOffset UploadExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record GetVideoQuery(Guid VideoId) : IQuery<VideoDetails>;

internal sealed class GetVideosValidator : IValidator<GetVideosQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetVideosQuery instance)
    {
        if (!PageLimits.IsValid(instance.PageNumber, instance.PageSize))
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }
    }
}

internal sealed class GetVideosHandler(IStreamingReadModel readModel) : IQueryHandler<GetVideosQuery, Page<VideoSummary>>
{
    public async Task<Result<Page<VideoSummary>>> Handle(GetVideosQuery query, CancellationToken cancellationToken)
    {
        var videos = readModel.Videos;
        if (query.ContentId is { } contentId)
        {
            videos = videos.Where(video => video.ContentId == contentId);
        }

        if (query.Status is { } status)
        {
            videos = videos.Where(video => video.Status == status);
        }

        var totalCount = await videos.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await videos
            .OrderByDescending(video => video.Id)
            .Skip(PageLimits.Offset(query.PageNumber, query.PageSize))
            .Take(query.PageSize)
            .Select(video => new VideoSummary(video.Id, video.ContentKind, video.ContentId, video.Status, video.DurationSeconds, video.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Page<VideoSummary>(items, query.PageNumber, query.PageSize, totalCount);
    }
}

internal sealed class GetVideoHandler(IStreamingReadModel readModel) : IQueryHandler<GetVideoQuery, VideoDetails>
{
    public async Task<Result<VideoDetails>> Handle(GetVideoQuery query, CancellationToken cancellationToken)
    {
        var video = await readModel.Videos.FirstOrDefaultAsync(item => item.Id == query.VideoId, cancellationToken).ConfigureAwait(false);
        if (video is null)
        {
            return VideoErrors.NotFound;
        }

        return new VideoDetails(
            video.Id,
            video.ContentKind,
            video.ContentId,
            video.Status,
            video.SizeBytes,
            video.DurationSeconds,
            [.. video.Renditions],
            video.FailureReason,
            video.UploadExpiresAt,
            video.CreatedAt);
    }
}
