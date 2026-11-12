using Shared.Domain;

namespace Streaming.Domain.Videos;

public enum ContentKind
{
    Movie,
    Episode,
}

public enum VideoStatus
{
    AwaitingUpload,
    Queued,
    Ready,
    Failed,

    /// <summary>The upload was never finished in time.</summary>
    Expired,
}

/// <summary>One quality of a converted video: a playlist of segments.</summary>
/// <param name="Playlist">Path of its playlist inside the video's folder, such as <c>720p/index.m3u8</c>.</param>
public sealed record Rendition(int Height, int Width, int Bandwidth, string Playlist);

/// <summary>
/// The video of a movie or an episode, from the promised upload to the playable result. A content
/// can have several: the latest that is ready is the one played.
/// </summary>
public sealed class Video : AggregateRoot, IAuditableEntity
{
    /// <summary>Time to finish an upload: large files over slow connections.</summary>
    public static readonly TimeSpan UploadLifetime = TimeSpan.FromHours(6);

    public const long MaxBytes = 10L * 1024 * 1024 * 1024;

    public const int FailureReasonMaxLength = 500;

    private readonly List<Rendition> _renditions = [];

    private Video(Guid id, ContentKind contentKind, Guid contentId, DateTimeOffset uploadExpiresAt)
        : base(id)
    {
        ContentKind = contentKind;
        ContentId = contentId;
        UploadExpiresAt = uploadExpiresAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Video()
    {
    }

    public ContentKind ContentKind { get; private set; }

    public Guid ContentId { get; private set; }

    public VideoStatus Status { get; private set; }

    public DateTimeOffset UploadExpiresAt { get; private set; }

    public long? SizeBytes { get; private set; }

    public double? DurationSeconds { get; private set; }

    public IReadOnlyCollection<Rendition> Renditions => _renditions.AsReadOnly();

    public string? FailureReason { get; private set; }

    /// <summary>Goes up with every change of status; events carry it.</summary>
    public int Revision { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public static Result<Video> StartUpload(ContentKind contentKind, Guid contentId, DateTimeOffset now)
    {
        if (!Enum.IsDefined(contentKind))
        {
            return VideoErrors.ContentKindInvalid;
        }

        if (contentId == Guid.Empty)
        {
            return VideoErrors.ContentRequired;
        }

        return new Video(Guid.CreateVersion7(), contentKind, contentId, now + UploadLifetime);
    }

    /// <summary>Accepts the uploaded file and puts the video in line for conversion.</summary>
    /// <param name="sizeBytes">The size of what arrived, or <see langword="null"/> when nothing did.</param>
    public Result ConfirmUpload(long? sizeBytes, DateTimeOffset now)
    {
        if (Status != VideoStatus.AwaitingUpload)
        {
            return VideoErrors.AlreadyConfirmed;
        }

        if (now >= UploadExpiresAt)
        {
            return VideoErrors.UploadExpired;
        }

        if (sizeBytes is not > 0)
        {
            return VideoErrors.NotUploaded;
        }

        if (sizeBytes > MaxBytes)
        {
            return VideoErrors.TooLarge;
        }

        SizeBytes = sizeBytes;
        ChangeStatus(VideoStatus.Queued);
        RaiseDomainEvent(new VideoQueuedDomainEvent(Id, ContentKind, ContentId, Revision));
        return Result.Success();
    }

    public Result MarkReady(double durationSeconds, IReadOnlyCollection<Rendition> renditions)
    {
        ArgumentNullException.ThrowIfNull(renditions);

        if (Status != VideoStatus.Queued)
        {
            return VideoErrors.NotQueued;
        }

        if (renditions.Count == 0 || durationSeconds <= 0)
        {
            return VideoErrors.NothingConverted;
        }

        DurationSeconds = durationSeconds;
        _renditions.Clear();
        _renditions.AddRange(renditions.OrderBy(rendition => rendition.Height));
        ChangeStatus(VideoStatus.Ready);
        RaiseDomainEvent(new VideoReadyDomainEvent(Id, ContentKind, ContentId, durationSeconds, Revision));
        return Result.Success();
    }

    public Result MarkFailed(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status != VideoStatus.Queued)
        {
            return VideoErrors.NotQueued;
        }

        FailureReason = reason.Length > FailureReasonMaxLength ? reason[..FailureReasonMaxLength] : reason;
        ChangeStatus(VideoStatus.Failed);
        RaiseDomainEvent(new VideoFailedDomainEvent(Id, ContentKind, ContentId, FailureReason, Revision));
        return Result.Success();
    }

    /// <summary>The upload was not finished in time; nothing outside the service heard of it.</summary>
    public void Expire(DateTimeOffset now)
    {
        if (Status == VideoStatus.AwaitingUpload && now >= UploadExpiresAt)
        {
            ChangeStatus(VideoStatus.Expired);
        }
    }

    private void ChangeStatus(VideoStatus status)
    {
        Status = status;
        Revision++;
    }
}

public static class VideoErrors
{
    public static readonly Error NotFound = Error.NotFound("Video.NotFound", "The video was not found.");

    public static readonly Error ContentKindInvalid = Error.Validation("Video.ContentKindInvalid", "The content kind must be Movie or Episode.");

    public static readonly Error ContentRequired = Error.Validation("Video.ContentRequired", "The content id is required.");

    public static readonly Error AlreadyConfirmed = Error.Conflict("Video.AlreadyConfirmed", "The upload of this video was already confirmed.");

    public static readonly Error UploadExpired = Error.Conflict("Video.UploadExpired", "This upload has expired. Start a new one.");

    public static readonly Error NotUploaded = Error.Conflict("Video.NotUploaded", "No file has arrived for this video yet.");

    public static readonly Error TooLarge = Error.Validation("Video.TooLarge", $"A video file may be at most {Video.MaxBytes / (1024 * 1024 * 1024)} GiB.");

    public static readonly Error NotQueued = Error.Conflict("Video.NotQueued", "The video is not waiting to be converted.");

    public static readonly Error NothingConverted = Error.Validation("Video.NothingConverted", "A converted video needs a duration and at least one rendition.");
}

public sealed record VideoQueuedDomainEvent(Guid VideoId, ContentKind ContentKind, Guid ContentId, int Revision) : IDomainEvent;

public sealed record VideoReadyDomainEvent(Guid VideoId, ContentKind ContentKind, Guid ContentId, double DurationSeconds, int Revision) : IDomainEvent;

public sealed record VideoFailedDomainEvent(Guid VideoId, ContentKind ContentKind, Guid ContentId, string Reason, int Revision) : IDomainEvent;
