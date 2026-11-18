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

    /// <summary>The video whose languages are kept: the newest reported, which may not be playable yet.</summary>
    public Guid? TracksVideoId { get; private set; }

    /// <summary>The revision of the languages kept; an older report for the same video changes nothing.</summary>
    public int TracksRevision { get; private set; }

    /// <summary>The audio languages, comma separated, the original first.</summary>
    public string AudioLanguageList { get; private set; } = string.Empty;

    /// <summary>The subtitle languages, comma separated.</summary>
    public string SubtitleLanguageList { get; private set; } = string.Empty;

    /// <summary>The languages of the sound viewers get; none until the playable video's are known.</summary>
    public IReadOnlyList<string> AudioLanguages => TracksVideoId is not null && TracksVideoId == PlayableVideoId ? Split(AudioLanguageList) : [];

    /// <summary>The languages of subtitles viewers get; none until the playable video's are known.</summary>
    public IReadOnlyList<string> SubtitleLanguages => TracksVideoId is not null && TracksVideoId == PlayableVideoId ? Split(SubtitleLanguageList) : [];

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

    /// <summary>
    /// Takes the languages a video offers. Reports can come in any order, before or after the one saying
    /// the video is ready: a newer video's replace an older one's, and a later revision a former one.
    /// </summary>
    internal void ApplyTracks(Guid videoId, IReadOnlyCollection<string> audio, IReadOnlyCollection<string> subtitles, int revision)
    {
        if (TracksVideoId is { } kept && (videoId.CompareTo(kept) < 0 || (videoId == kept && revision <= TracksRevision)))
        {
            return;
        }

        TracksVideoId = videoId;
        TracksRevision = revision;
        AudioLanguageList = string.Join(',', audio);
        SubtitleLanguageList = string.Join(',', subtitles);
    }

    private static string[] Split(string list) => list.Length == 0 ? [] : list.Split(',');
}
