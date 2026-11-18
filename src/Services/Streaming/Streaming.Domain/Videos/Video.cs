using System.Globalization;
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

    /// <summary>A dub as long as the video, give or take this much.</summary>
    public static readonly TimeSpan AudioDurationTolerance = TimeSpan.FromSeconds(2);

    private readonly List<Rendition> _renditions = [];

    private readonly List<AudioTrack> _audioTracks = [];

    private readonly List<Subtitle> _subtitles = [];

    private Video(Guid id, ContentKind contentKind, Guid contentId, string originalAudioLanguage, DateTimeOffset uploadExpiresAt)
        : base(id)
    {
        ContentKind = contentKind;
        ContentId = contentId;
        OriginalAudioLanguage = originalAudioLanguage;
        AudioLayout = AudioLayout.Separate;
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

    /// <summary>Videos converted before audio tracks keep their sound inside the renditions.</summary>
    public AudioLayout AudioLayout { get; private set; }

    /// <summary>The language of the sound in the uploaded file.</summary>
    public string OriginalAudioLanguage { get; private set; } = MediaLanguages.Default;

    public IReadOnlyCollection<AudioTrack> AudioTracks => _audioTracks.AsReadOnly();

    public IReadOnlyCollection<Subtitle> Subtitles => _subtitles.AsReadOnly();

    public string? FailureReason { get; private set; }

    /// <summary>Goes up with every change of status; events carry it.</summary>
    public int Revision { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public static Result<Video> StartUpload(ContentKind contentKind, Guid contentId, string originalAudioLanguage, DateTimeOffset now)
    {
        if (!MediaLanguages.IsSupported(originalAudioLanguage))
        {
            return VideoErrors.LanguageUnsupported;
        }

        if (!Enum.IsDefined(contentKind))
        {
            return VideoErrors.ContentKindInvalid;
        }

        if (contentId == Guid.Empty)
        {
            return VideoErrors.ContentRequired;
        }

        return new Video(Guid.CreateVersion7(), contentKind, contentId, originalAudioLanguage, now + UploadLifetime);
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

    /// <param name="originalAudioBandwidth">
    /// The bits per second of the file's own sound, converted into <c>audio/original</c>; <see langword="null"/> when the file is silent.
    /// </param>
    public Result MarkReady(double durationSeconds, IReadOnlyCollection<Rendition> renditions, int? originalAudioBandwidth = null)
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
        if (AudioLayout == AudioLayout.Separate && originalAudioBandwidth is { } bandwidth)
        {
            var original = new AudioTrack(Guid.CreateVersion7(), OriginalAudioLanguage, isOriginal: true, UploadExpiresAt);
            original.MarkReady(bandwidth);
            _audioTracks.Add(original);
        }

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

    /// <summary>
    /// Promises the upload of a dub: one track per language of <see cref="MediaLanguages"/>. A failed track, or one whose upload expired, of the same language
    /// makes room for it.
    /// </summary>
    public Result<AudioTrack> StartAudioTrack(string language, DateTimeOffset now)
    {
        if (!MediaLanguages.IsSupported(language))
        {
            return VideoErrors.LanguageUnsupported;
        }

        if (Status != VideoStatus.Ready)
        {
            return VideoErrors.NotReady;
        }

        if (AudioLayout != AudioLayout.Separate)
        {
            return VideoErrors.LayoutOutdated;
        }

        _audioTracks.RemoveAll(track => track.Language == language && track.IsReplaceable(now));
        if (_audioTracks.Exists(track => track.Language == language))
        {
            return VideoErrors.AudioTrackExists;
        }

        var track = new AudioTrack(Guid.CreateVersion7(), language, isOriginal: false, now + UploadLifetime);
        _audioTracks.Add(track);
        return track;
    }

    /// <summary>Accepts the uploaded dub and puts it in line for conversion.</summary>
    public Result ConfirmAudioTrack(Guid trackId, long? sizeBytes, DateTimeOffset now)
    {
        var track = _audioTracks.Find(item => item.Id == trackId);
        if (track is null)
        {
            return VideoErrors.AudioTrackNotFound;
        }

        if (track.Status != TrackStatus.AwaitingUpload)
        {
            return VideoErrors.AlreadyConfirmed;
        }

        if (now >= track.UploadExpiresAt)
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

        track.Queue();
        return Result.Success();
    }

    /// <summary>The converted dub. One whose length is not the video's is refused: it would not stay in sync.</summary>
    public Result MarkAudioTrackReady(Guid trackId, double durationSeconds, int bandwidth)
    {
        var track = _audioTracks.Find(item => item.Id == trackId);
        if (track is null)
        {
            return VideoErrors.AudioTrackNotFound;
        }

        if (track.Status != TrackStatus.Queued)
        {
            return VideoErrors.NotQueued;
        }

        if (Math.Abs(durationSeconds - (DurationSeconds ?? 0)) > AudioDurationTolerance.TotalSeconds)
        {
            track.MarkFailed(string.Create(CultureInfo.InvariantCulture, $"The audio lasts {durationSeconds:0.#} s, the video {DurationSeconds:0.#} s."));
            return Result.Success();
        }

        track.MarkReady(bandwidth);
        return Result.Success();
    }

    public Result MarkAudioTrackFailed(Guid trackId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var track = _audioTracks.Find(item => item.Id == trackId);
        if (track is null)
        {
            return VideoErrors.AudioTrackNotFound;
        }

        if (track.Status != TrackStatus.Queued)
        {
            return VideoErrors.NotQueued;
        }

        track.MarkFailed(reason);
        return Result.Success();
    }

    /// <summary>Takes a dub off; the original stays, since the video would be silent without it.</summary>
    public Result RemoveAudioTrack(string language)
    {
        var track = _audioTracks.Find(item => item.Language == language);
        if (track is null)
        {
            return VideoErrors.AudioTrackNotFound;
        }

        if (track.IsOriginal)
        {
            return VideoErrors.OriginalAudioKept;
        }

        _audioTracks.Remove(track);
        return Result.Success();
    }

    /// <summary>
    /// Promises the upload of subtitles in a language the video does not have yet; one whose upload
    /// expired makes room for it.
    /// </summary>
    public Result<Subtitle> StartSubtitle(string language, DateTimeOffset now)
    {
        if (!MediaLanguages.IsSupported(language))
        {
            return VideoErrors.LanguageUnsupported;
        }

        if (Status != VideoStatus.Ready)
        {
            return VideoErrors.NotReady;
        }

        _subtitles.RemoveAll(subtitle => subtitle.Language == language && subtitle.IsReplaceable(now));
        if (_subtitles.Exists(subtitle => subtitle.Language == language))
        {
            return VideoErrors.SubtitleExists;
        }

        var created = new Subtitle(Guid.CreateVersion7(), language, now + UploadLifetime);
        _subtitles.Add(created);
        return created;
    }

    /// <summary>The subtitles waiting for this upload, before their file is read.</summary>
    public Result<Subtitle> SubtitleAwaitingUpload(Guid subtitleId, DateTimeOffset now)
    {
        var subtitle = _subtitles.Find(item => item.Id == subtitleId);
        if (subtitle is null)
        {
            return VideoErrors.SubtitleNotFound;
        }

        if (subtitle.Status != TrackStatus.AwaitingUpload)
        {
            return VideoErrors.AlreadyConfirmed;
        }

        if (now >= subtitle.UploadExpiresAt)
        {
            return VideoErrors.UploadExpired;
        }

        return subtitle;
    }

    /// <summary>The uploaded file was read and written as WebVTT to <see cref="Subtitle.Target"/>.</summary>
    public Result AcceptSubtitle(Guid subtitleId, DateTimeOffset now)
    {
        var subtitle = SubtitleAwaitingUpload(subtitleId, now);
        if (subtitle.IsFailure)
        {
            return subtitle.Error;
        }

        subtitle.Value.Accept();
        return Result.Success();
    }

    public Result RemoveSubtitle(string language)
        => _subtitles.RemoveAll(item => item.Language == language) > 0 ? Result.Success() : VideoErrors.SubtitleNotFound;

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

    public static readonly Error LanguageUnsupported = Error.Validation("Video.LanguageUnsupported", $"The language must be one of {string.Join(", ", MediaLanguages.Names.Keys)}.");

    public static readonly Error NotReady = Error.Conflict("Video.NotReady", "The video is not converted yet.");

    public static readonly Error LayoutOutdated = Error.Conflict("Video.LayoutOutdated", "This video was converted with its sound inside every quality; upload it again to add languages.");

    public static readonly Error AudioTrackExists = Error.Conflict("Video.AudioTrackExists", "The video already has a track in this language. Remove it first.");

    public static readonly Error AudioTrackNotFound = Error.NotFound("Video.AudioTrackNotFound", "The video has no such audio track.");

    public static readonly Error SubtitleExists = Error.Conflict("Video.SubtitleExists", "The video already has subtitles in this language. Remove them first.");

    public static readonly Error SubtitleNotFound = Error.NotFound("Video.SubtitleNotFound", "The video has no such subtitles.");

    public static readonly Error SubtitleInvalid = Error.Validation("Video.SubtitleInvalid", "The file is not WebVTT or SRT subtitles with at least one cue.");

    public static readonly Error SubtitleTooLarge = Error.Validation("Video.SubtitleTooLarge", $"A subtitle file may be at most {Subtitle.MaxBytes / 1024} KiB.");

    public static readonly Error OriginalAudioKept = Error.Conflict("Video.OriginalAudioKept", "The video's original sound cannot be removed.");

    public static readonly Error NothingConverted = Error.Validation("Video.NothingConverted", "A converted video needs a duration and at least one rendition.");
}

public sealed record VideoQueuedDomainEvent(Guid VideoId, ContentKind ContentKind, Guid ContentId, int Revision) : IDomainEvent;

public sealed record VideoReadyDomainEvent(Guid VideoId, ContentKind ContentKind, Guid ContentId, double DurationSeconds, int Revision) : IDomainEvent;

public sealed record VideoFailedDomainEvent(Guid VideoId, ContentKind ContentKind, Guid ContentId, string Reason, int Revision) : IDomainEvent;
