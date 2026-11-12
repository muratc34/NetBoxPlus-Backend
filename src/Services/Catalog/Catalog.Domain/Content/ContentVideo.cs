namespace Catalog.Domain.Content;

/// <summary>Where the video of a movie or an episode stands, as the streaming service reports it.</summary>
public enum VideoState
{
    None,
    Processing,
    Ready,
    Failed,
}

/// <summary>
/// The video of a movie or an episode. The latest upload's state is what the admin panel shows; the
/// playable video is the latest that became ready, kept while a newer one is converted or fails.
/// Video ids are version 7 Guids, so a later upload has a greater id.
/// </summary>
public sealed class ContentVideo
{
    public Guid? LatestVideoId { get; private set; }

    public VideoState LatestState { get; private set; }

    /// <summary>The revision of the latest video's state; an older message for it changes nothing.</summary>
    public int LatestRevision { get; private set; }

    public Guid? PlayableVideoId { get; private set; }

    public bool IsPlayable => PlayableVideoId is not null;

    /// <summary>Takes a report on one video; messages can come late or twice.</summary>
    internal void Apply(Guid videoId, VideoState state, int revision)
    {
        if (LatestVideoId is null || videoId.CompareTo(LatestVideoId.Value) > 0)
        {
            LatestVideoId = videoId;
            LatestState = state;
            LatestRevision = revision;
        }
        else if (videoId == LatestVideoId && revision > LatestRevision)
        {
            LatestState = state;
            LatestRevision = revision;
        }

        if (state == VideoState.Ready && (PlayableVideoId is null || videoId.CompareTo(PlayableVideoId.Value) > 0))
        {
            PlayableVideoId = videoId;
        }
    }
}
