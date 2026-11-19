namespace Streaming.Domain.Videos;

/// <summary>
/// One language of a video's subtitles: a WebVTT file in the video's folder. It needs no conversion
/// by the transcoder, so it can be added to any converted video, those of the old layout too.
/// </summary>
public sealed class Subtitle
{
    /// <summary>The largest subtitle file accepted.</summary>
    public const long MaxBytes = 1024 * 1024;

    public const int PathMaxLength = 64;

    internal Subtitle(Guid id, string language, DateTimeOffset uploadExpiresAt)
    {
        Id = id;
        Language = language;
        UploadExpiresAt = uploadExpiresAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Subtitle()
    {
        Language = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>A tag of <see cref="MediaLanguages"/>.</summary>
    public string Language { get; private set; }

    public TrackStatus Status { get; private set; }

    public DateTimeOffset UploadExpiresAt { get; private set; }

    /// <summary>Path of the WebVTT file inside the video's folder, once accepted.</summary>
    public string? File { get; private set; }

    /// <summary>Where the accepted file is written inside the video's folder.</summary>
    public string Target => $"subtitles/{Id:N}.vtt";

    internal bool IsReplaceable(DateTimeOffset now) => Status == TrackStatus.AwaitingUpload && now >= UploadExpiresAt;

    internal void Accept()
    {
        File = Target;
        Status = TrackStatus.Ready;
    }
}
