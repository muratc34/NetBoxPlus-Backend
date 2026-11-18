namespace Streaming.Domain.Videos;

/// <summary>How a converted video carries its sound.</summary>
public enum AudioLayout
{
    /// <summary>Converted before audio tracks: the sound is inside every rendition, and no other language can be added.</summary>
    Muxed,

    /// <summary>The renditions are silent; each language is an audio rendition of its own.</summary>
    Separate,
}

public enum TrackStatus
{
    AwaitingUpload,
    Queued,
    Ready,
    Failed,
}

/// <summary>
/// One language of a video's sound. The original comes from the video's own file; a dub is uploaded
/// later and converted on its own. Its playlist lives in the video's folder.
/// </summary>
public sealed class AudioTrack
{
    /// <summary>The folder of the sound that came with the video's file.</summary>
    public const string OriginalFolder = "original";

    public const int PlaylistMaxLength = 64;

    internal AudioTrack(Guid id, string language, bool isOriginal, DateTimeOffset uploadExpiresAt)
    {
        Id = id;
        Language = language;
        IsOriginal = isOriginal;
        UploadExpiresAt = uploadExpiresAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private AudioTrack()
    {
        Language = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>A tag of <see cref="MediaLanguages"/>.</summary>
    public string Language { get; private set; }

    /// <summary>The sound of the video's own file; it cannot be removed.</summary>
    public bool IsOriginal { get; private set; }

    public TrackStatus Status { get; private set; }

    public DateTimeOffset UploadExpiresAt { get; private set; }

    /// <summary>Bits per second, once converted.</summary>
    public int? Bandwidth { get; private set; }

    /// <summary>Path of its playlist inside the video's folder, such as <c>audio/original/index.m3u8</c>.</summary>
    public string? Playlist { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>The folder its files go to inside the video's folder.</summary>
    public string Folder => $"audio/{(IsOriginal ? OriginalFolder : Id.ToString("N"))}";

    /// <summary>Whether a new upload of the same language may take its place.</summary>
    internal bool IsReplaceable(DateTimeOffset now)
        => Status == TrackStatus.Failed || (Status == TrackStatus.AwaitingUpload && now >= UploadExpiresAt);

    internal void Queue() => Status = TrackStatus.Queued;

    internal void MarkReady(int bandwidth)
    {
        Bandwidth = bandwidth;
        Playlist = $"{Folder}/index.m3u8";
        FailureReason = null;
        Status = TrackStatus.Ready;
    }

    internal void MarkFailed(string reason)
    {
        FailureReason = reason.Length > Video.FailureReasonMaxLength ? reason[..Video.FailureReasonMaxLength] : reason;
        Status = TrackStatus.Failed;
    }
}
