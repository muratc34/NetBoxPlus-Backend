using System.Net;
using Microsoft.Extensions.Logging;
using NetBoxPlus.DemoSeeder.Content;
using NetBoxPlus.DemoSeeder.Gateway;
using NetBoxPlus.DemoSeeder.Media;

namespace NetBoxPlus.DemoSeeder.Seeding;

/// <summary>
/// Puts the demo catalog in place through the admin API: genres, people, movies and series with
/// their images and videos, then publishes them once the videos are ready. Every step first looks at
/// what is there, so a second run only finishes what is missing.
/// </summary>
internal sealed class CatalogSeeder(
    GatewayClient gateway,
    BlobUploader uploader,
    MediaFactory media,
    SeederOptions options,
    TimeProvider time,
    ILogger<CatalogSeeder> logger)
{
    private const string Catalog = "/admin/catalog";
    private const string Streaming = "/admin/streaming";

    private readonly Dictionary<string, Guid> _genres = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _people = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _pendingVideos = [];

    public int Changes { get; private set; }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await SeedGenresAsync(cancellationToken).ConfigureAwait(false);
        foreach (var person in DemoContent.People)
        {
            await SeedPersonAsync(person, cancellationToken).ConfigureAwait(false);
        }

        var movies = new List<(MovieSeed Seed, Guid Id)>();
        foreach (var movie in DemoContent.Movies)
        {
            movies.Add((movie, await SeedMovieAsync(movie, cancellationToken).ConfigureAwait(false)));
        }

        var series = new List<(SeriesSeed Seed, Guid Id)>();
        foreach (var show in DemoContent.Series)
        {
            series.Add((show, await SeedSeriesAsync(show, cancellationToken).ConfigureAwait(false)));
        }

        await WaitForVideosAsync(cancellationToken).ConfigureAwait(false);

        foreach (var (seed, id) in movies.Where(movie => movie.Seed.Dubbed))
        {
            await SeedLanguagesAsync(seed, id, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (seed, id) in movies)
        {
            await PublishMovieAsync(seed, id, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (seed, id) in series)
        {
            await PublishSeriesAsync(seed, id, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SeedGenresAsync(CancellationToken cancellationToken)
    {
        var existing = await gateway.GetAsync<List<GenreItem>>($"{Catalog}/genres", cancellationToken).ConfigureAwait(false);
        foreach (var genre in DemoContent.Genres)
        {
            var found = existing.FirstOrDefault(item => item.Translations.TryGetValue("tr", out var name) && name == genre.Turkish);
            if (found is not null)
            {
                _genres[genre.Turkish] = found.Id;
                continue;
            }

            var created = await gateway.PostAsync<Created>(
                $"{Catalog}/genres",
                new { translations = new Dictionary<string, string> { ["tr"] = genre.Turkish, ["en"] = genre.English } },
                cancellationToken).ConfigureAwait(false);
            _genres[genre.Turkish] = created.Id;
            Changed("Genre", genre.Turkish);
        }
    }

    private async Task SeedPersonAsync(PersonSeed person, CancellationToken cancellationToken)
    {
        var page = await gateway.GetAsync<Page<PersonItem>>(
            $"{Catalog}/people?search={Uri.EscapeDataString(person.Name)}&pageSize=100", cancellationToken).ConfigureAwait(false);
        var found = page.Items.FirstOrDefault(item => item.Name == person.Name);
        Guid id;
        if (found is null)
        {
            var created = await gateway.PostAsync<Created>(
                $"{Catalog}/people",
                new { name = person.Name, translations = new Dictionary<string, string> { ["tr"] = person.BiographyTurkish, ["en"] = person.BiographyEnglish } },
                cancellationToken).ConfigureAwait(false);
            id = created.Id;
            Changed("Person", person.Name);
        }
        else
        {
            id = found.Id;
        }

        _people[person.Name] = id;
        if (SeedDecisions.NeedsImage(found?.PhotoUrl))
        {
            var uploadId = await UploadImageAsync(await media.PhotoAsync(person.Name, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            await gateway.PutAsync($"{Catalog}/people/{id}/photo", new { uploadId }, cancellationToken).ConfigureAwait(false);
            Changed("Person", person.Name, "photo attached");
        }
    }

    private async Task<Guid> SeedMovieAsync(MovieSeed movie, CancellationToken cancellationToken)
    {
        var id = await FindTitleAsync("movies", movie.Turkish.Title, movie.OriginalTitle, cancellationToken).ConfigureAwait(false);
        if (id is null)
        {
            var created = await gateway.PostAsync<Created>(
                $"{Catalog}/movies",
                new
                {
                    translations = TextsOf(movie.Turkish, movie.English),
                    originalTitle = movie.OriginalTitle,
                    releaseYear = movie.ReleaseYear,
                    ageRating = movie.AgeRating,
                    runtimeMinutes = movie.RuntimeMinutes,
                    genreIds = movie.Genres.Select(name => _genres[name]).ToList(),
                    credits = CreditsOf(movie.Credits),
                    version = 0,
                },
                cancellationToken).ConfigureAwait(false);
            id = created.Id;
            Changed("Movie", movie.OriginalTitle);
        }

        var path = $"{Catalog}/movies/{id}";
        var details = await gateway.GetAsync<MovieItem>(path, cancellationToken).ConfigureAwait(false);
        if (SeedDecisions.NeedsImage(details.PosterUrl))
        {
            await AttachImageAsync(path, "poster", await media.PosterAsync(movie.Turkish.Title, movie.Palette, cancellationToken).ConfigureAwait(false), details.Version, cancellationToken).ConfigureAwait(false);
            Changed("Movie", movie.OriginalTitle, "poster attached");
            details = await gateway.GetAsync<MovieItem>(path, cancellationToken).ConfigureAwait(false);
        }

        if (SeedDecisions.NeedsImage(details.BackdropUrl))
        {
            await AttachImageAsync(path, "backdrop", await media.BackdropAsync(movie.Turkish.Title, movie.Palette, cancellationToken).ConfigureAwait(false), details.Version, cancellationToken).ConfigureAwait(false);
            Changed("Movie", movie.OriginalTitle, "backdrop attached");
        }

        if (!details.HasPlayableVideo)
        {
            await EnsureVideoAsync("Movie", id.Value, movie.OriginalTitle, movie.Turkish.Title, movie.VideoHeight, cancellationToken).ConfigureAwait(false);
        }

        return id.Value;
    }

    private async Task<Guid> SeedSeriesAsync(SeriesSeed series, CancellationToken cancellationToken)
    {
        var id = await FindTitleAsync("series", series.Turkish.Title, series.OriginalTitle, cancellationToken).ConfigureAwait(false);
        if (id is null)
        {
            var created = await gateway.PostAsync<Created>(
                $"{Catalog}/series",
                new
                {
                    translations = TextsOf(series.Turkish, series.English),
                    originalTitle = series.OriginalTitle,
                    releaseYear = series.ReleaseYear,
                    ageRating = series.AgeRating,
                    genreIds = series.Genres.Select(name => _genres[name]).ToList(),
                    credits = CreditsOf(series.Credits),
                    version = 0,
                },
                cancellationToken).ConfigureAwait(false);
            id = created.Id;
            Changed("Series", series.OriginalTitle);
        }

        var path = $"{Catalog}/series/{id}";
        var details = await gateway.GetAsync<SeriesItem>(path, cancellationToken).ConfigureAwait(false);
        if (SeedDecisions.NeedsImage(details.PosterUrl))
        {
            await AttachImageAsync(path, "poster", await media.PosterAsync(series.Turkish.Title, series.Palette, cancellationToken).ConfigureAwait(false), details.Version, cancellationToken).ConfigureAwait(false);
            Changed("Series", series.OriginalTitle, "poster attached");
            details = await gateway.GetAsync<SeriesItem>(path, cancellationToken).ConfigureAwait(false);
        }

        if (SeedDecisions.NeedsImage(details.BackdropUrl))
        {
            await AttachImageAsync(path, "backdrop", await media.BackdropAsync(series.Turkish.Title, series.Palette, cancellationToken).ConfigureAwait(false), details.Version, cancellationToken).ConfigureAwait(false);
            Changed("Series", series.OriginalTitle, "backdrop attached");
            details = await gateway.GetAsync<SeriesItem>(path, cancellationToken).ConfigureAwait(false);
        }

        foreach (var season in series.Seasons)
        {
            if (details.Seasons.All(item => item.Number != season.Number))
            {
                await gateway.PostAsync<Created>(
                    $"{path}/seasons",
                    new { number = season.Number, translations = new Dictionary<string, string> { ["tr"] = season.TitleTurkish, ["en"] = season.TitleEnglish }, version = details.Version },
                    cancellationToken).ConfigureAwait(false);
                Changed("Series", series.OriginalTitle, $"season {season.Number} added");
                details = await gateway.GetAsync<SeriesItem>(path, cancellationToken).ConfigureAwait(false);
            }

            var seasonId = details.Seasons.Single(item => item.Number == season.Number).Id;
            foreach (var episode in season.Episodes)
            {
                if (details.Seasons.Single(item => item.Id == seasonId).Episodes.All(item => item.Number != episode.Number))
                {
                    await gateway.PostAsync<Created>(
                        $"{path}/seasons/{seasonId}/episodes",
                        new { number = episode.Number, translations = TextsOf(episode.Turkish, episode.English), runtimeMinutes = episode.RuntimeMinutes, version = details.Version },
                        cancellationToken).ConfigureAwait(false);
                    Changed("Series", series.OriginalTitle, $"S{season.Number}E{episode.Number} added");
                    details = await gateway.GetAsync<SeriesItem>(path, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        foreach (var season in series.Seasons)
        {
            var stored = details.Seasons.Single(item => item.Number == season.Number);
            foreach (var episode in season.Episodes)
            {
                var storedEpisode = stored.Episodes.Single(item => item.Number == episode.Number);
                if (!storedEpisode.HasPlayableVideo)
                {
                    await EnsureVideoAsync(
                        "Episode",
                        storedEpisode.Id,
                        $"{series.OriginalTitle} S{season.Number}E{episode.Number}",
                        $"{series.Turkish.Title}\n{season.Number}. Sezon {episode.Number}. Bölüm",
                        720,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return id.Value;
    }

    /// <summary>Finds a movie or series by its Turkish title, then makes sure it is the demo one by its original title.</summary>
    private async Task<Guid?> FindTitleAsync(string collection, string title, string originalTitle, CancellationToken cancellationToken)
    {
        var page = await gateway.GetAsync<Page<TitleItem>>(
            $"{Catalog}/{collection}?search={Uri.EscapeDataString(title)}&pageSize=100", cancellationToken).ConfigureAwait(false);
        foreach (var candidate in page.Items.Where(item => item.Title == title))
        {
            var details = await gateway.GetAsync<MovieItem>($"{Catalog}/{collection}/{candidate.Id}", cancellationToken).ConfigureAwait(false);
            if (details.OriginalTitle == originalTitle)
            {
                return candidate.Id;
            }
        }

        return null;
    }

    private async Task EnsureVideoAsync(string contentKind, Guid contentId, string name, string caption, int height, CancellationToken cancellationToken)
    {
        var videos = await gateway.GetAsync<Page<VideoItem>>($"{Streaming}/videos?contentId={contentId}&pageSize=100", cancellationToken).ConfigureAwait(false);
        switch (SeedDecisions.ForVideo(videos.Items))
        {
            case VideoStep.Ready:
                // Ready in streaming, not yet playable in the catalog: the event is on its way.
                return;
            case VideoStep.Wait wait:
                _pendingVideos.Add(wait.VideoId);
                return;
            default:
                var file = await media.VideoAsync(caption, height, cancellationToken).ConfigureAwait(false);
                var ticket = await gateway.PostAsync<VideoUploadTicket>($"{Streaming}/videos", new { contentKind, contentId }, cancellationToken).ConfigureAwait(false);
                await uploader.UploadAsync(ticket.UploadAddress, file, "video/mp4", cancellationToken).ConfigureAwait(false);
                await gateway.PostAsync($"{Streaming}/videos/{ticket.VideoId}/uploaded", body: null, cancellationToken).ConfigureAwait(false);
                _pendingVideos.Add(ticket.VideoId);
                Changed(contentKind, name, "video uploaded");
                return;
        }
    }

    /// <summary>
    /// An English dub and Turkish and English subtitles for the movie's ready video. A video converted
    /// before audio tracks is uploaded again first, since only the new layout takes a dub.
    /// </summary>
    private async Task SeedLanguagesAsync(MovieSeed movie, Guid id, CancellationToken cancellationToken)
    {
        var video = await ReadyVideoAsync(id, cancellationToken).ConfigureAwait(false);
        var plan = SeedDecisions.ForLanguages(video);
        if (plan.ConvertAgain)
        {
            var file = await media.VideoAsync(movie.Turkish.Title, movie.VideoHeight, cancellationToken).ConfigureAwait(false);
            var ticket = await gateway.PostAsync<VideoUploadTicket>($"{Streaming}/videos", new { contentKind = "Movie", contentId = id }, cancellationToken).ConfigureAwait(false);
            await uploader.UploadAsync(ticket.UploadAddress, file, "video/mp4", cancellationToken).ConfigureAwait(false);
            await gateway.PostAsync($"{Streaming}/videos/{ticket.VideoId}/uploaded", body: null, cancellationToken).ConfigureAwait(false);
            _pendingVideos.Add(ticket.VideoId);
            Changed("Movie", movie.OriginalTitle, "video uploaded again for its languages");
            await WaitForVideosAsync(cancellationToken).ConfigureAwait(false);
            video = await gateway.GetAsync<VideoDetails>($"{Streaming}/videos/{ticket.VideoId}", cancellationToken).ConfigureAwait(false);
            plan = SeedDecisions.ForLanguages(video);
        }

        var path = $"{Streaming}/videos/{video.Id}";
        if (plan.Dub != TrackStep.None)
        {
            if (plan.Dub == TrackStep.ReplaceAndUpload)
            {
                await gateway.DeleteAsync($"{path}/audio-tracks/{SeedDecisions.DubLanguage}", cancellationToken).ConfigureAwait(false);
            }

            var ticket = await gateway.PostAsync<AudioTrackUploadTicket>($"{path}/audio-tracks", new { language = SeedDecisions.DubLanguage }, cancellationToken).ConfigureAwait(false);
            await uploader.UploadAsync(ticket.UploadAddress, await media.DubAsync(cancellationToken).ConfigureAwait(false), "audio/mp4", cancellationToken).ConfigureAwait(false);
            await gateway.PostAsync($"{path}/audio-tracks/{ticket.TrackId}/uploaded", body: null, cancellationToken).ConfigureAwait(false);
            Changed("Movie", movie.OriginalTitle, $"{SeedDecisions.DubLanguage} dub uploaded");
        }

        foreach (var (language, step) in plan.Subtitles.Where(item => item.Value != TrackStep.None))
        {
            if (step == TrackStep.ReplaceAndUpload)
            {
                await gateway.DeleteAsync($"{path}/subtitles/{language}", cancellationToken).ConfigureAwait(false);
            }

            var ticket = await gateway.PostAsync<SubtitleUploadTicket>($"{path}/subtitles", new { language }, cancellationToken).ConfigureAwait(false);
            await uploader.UploadAsync(ticket.UploadAddress, await media.SubtitlesAsync(language, cancellationToken).ConfigureAwait(false), "application/x-subrip", cancellationToken).ConfigureAwait(false);
            await gateway.PostAsync($"{path}/subtitles/{ticket.SubtitleId}/uploaded", body: null, cancellationToken).ConfigureAwait(false);
            Changed("Movie", movie.OriginalTitle, $"{language} subtitles added");
        }

        await WaitForDubAsync(video.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<VideoDetails> ReadyVideoAsync(Guid contentId, CancellationToken cancellationToken)
    {
        var videos = await gateway.GetAsync<Page<VideoItem>>($"{Streaming}/videos?contentId={contentId}&status=Ready&pageSize=100", cancellationToken).ConfigureAwait(false);
        var latest = videos.Items.MaxBy(item => item.CreatedAt) ?? throw new InvalidOperationException($"Movie {contentId} has no ready video.");
        return await gateway.GetAsync<VideoDetails>($"{Streaming}/videos/{latest.Id}", cancellationToken).ConfigureAwait(false);
    }

    private async Task WaitForDubAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + options.TranscodeTimeout;
        while (true)
        {
            var video = await gateway.GetAsync<VideoDetails>($"{Streaming}/videos/{videoId}", cancellationToken).ConfigureAwait(false);
            var dub = video.AudioTracks?.FirstOrDefault(track => track.Language == SeedDecisions.DubLanguage);
            if (dub?.Status is null or "Ready")
            {
                return;
            }

            if (dub.Status == "Failed")
            {
                throw new InvalidOperationException($"The dub of video {videoId} failed: {dub.FailureReason}");
            }

            if (time.GetUtcNow() > deadline)
            {
                throw new TimeoutException($"The dub of video {videoId} was not converted within {options.TranscodeTimeout}. Is the transcoder running?");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), time, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForVideosAsync(CancellationToken cancellationToken)
    {
        if (_pendingVideos.Count == 0)
        {
            return;
        }

        SeederLog.WaitingForVideos(logger, _pendingVideos.Count);
        var deadline = time.GetUtcNow() + options.TranscodeTimeout;
        var total = _pendingVideos.Count;
        while (_pendingVideos.Count > 0)
        {
            foreach (var videoId in _pendingVideos.ToList())
            {
                var video = await gateway.GetAsync<VideoDetails>($"{Streaming}/videos/{videoId}", cancellationToken).ConfigureAwait(false);
                if (video.Status == "Ready")
                {
                    _pendingVideos.Remove(videoId);
                }
                else if (video.Status != "Queued")
                {
                    throw new InvalidOperationException($"Video {videoId} is {video.Status}: {video.FailureReason}");
                }
            }

            SeederLog.VideosProgress(logger, total - _pendingVideos.Count, total);
            if (_pendingVideos.Count == 0)
            {
                break;
            }

            if (time.GetUtcNow() > deadline)
            {
                throw new TimeoutException($"{_pendingVideos.Count} videos were not transcoded within {options.TranscodeTimeout}. Is the transcoder running?");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), time, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PublishMovieAsync(MovieSeed movie, Guid id, CancellationToken cancellationToken)
    {
        var details = await gateway.GetAsync<MovieItem>($"{Catalog}/movies/{id}", cancellationToken).ConfigureAwait(false);
        if (SeedDecisions.NeedsPublishing(details.Status))
        {
            await UntilPlayableAsync(() => gateway.PostAsync($"{Catalog}/movies/{id}/publish", body: null, cancellationToken), cancellationToken).ConfigureAwait(false);
            Changed("Movie", movie.OriginalTitle, "published");
        }
    }

    private async Task PublishSeriesAsync(SeriesSeed series, Guid id, CancellationToken cancellationToken)
    {
        var path = $"{Catalog}/series/{id}";
        var details = await gateway.GetAsync<SeriesItem>(path, cancellationToken).ConfigureAwait(false);
        foreach (var episode in details.Seasons.OrderBy(season => season.Number).SelectMany(season => season.Episodes.OrderBy(item => item.Number).Select(item => (season.Number, Episode: item))))
        {
            if (SeedDecisions.NeedsPublishing(episode.Episode.Status))
            {
                await UntilPlayableAsync(
                    async () =>
                    {
                        var version = (await gateway.GetAsync<SeriesItem>(path, cancellationToken).ConfigureAwait(false)).Version;
                        await gateway.PostAsync($"{path}/episodes/{episode.Episode.Id}/publish?version={version}", body: null, cancellationToken).ConfigureAwait(false);
                    },
                    cancellationToken).ConfigureAwait(false);
                Changed("Series", series.OriginalTitle, $"S{episode.Number}E{episode.Episode.Number} published");
            }
        }

        details = await gateway.GetAsync<SeriesItem>(path, cancellationToken).ConfigureAwait(false);
        if (SeedDecisions.NeedsPublishing(details.Status))
        {
            await gateway.PostAsync($"{path}/publish", body: null, cancellationToken).ConfigureAwait(false);
            Changed("Series", series.OriginalTitle, "published");
        }
    }

    /// <summary>
    /// A transcoded video reaches the catalog through an event, a moment after streaming calls it
    /// ready; until then publishing answers Content.VideoRequired. Tries again for up to two minutes.
    /// </summary>
    private async Task UntilPlayableAsync(Func<Task> publish, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + TimeSpan.FromMinutes(2);
        while (true)
        {
            try
            {
                await publish().ConfigureAwait(false);
                return;
            }
            catch (GatewayException exception) when (exception is { Status: HttpStatusCode.Conflict, Code: "Content.VideoRequired" } && time.GetUtcNow() < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task AttachImageAsync(string titlePath, string slot, string file, int version, CancellationToken cancellationToken)
    {
        var uploadId = await UploadImageAsync(file, cancellationToken).ConfigureAwait(false);
        await gateway.PutAsync($"{titlePath}/images/{slot}", new { uploadId, version }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid> UploadImageAsync(string file, CancellationToken cancellationToken)
    {
        var ticket = await gateway.PostAsync<ImageUploadTicket>($"{Catalog}/images/uploads", body: null, cancellationToken).ConfigureAwait(false);
        await uploader.UploadAsync(ticket.UploadAddress, file, "image/png", cancellationToken).ConfigureAwait(false);
        return ticket.UploadId;
    }

    private static Dictionary<string, object> TextsOf(Texts turkish, Texts english) => new()
    {
        ["tr"] = new { title = turkish.Title, synopsis = turkish.Synopsis },
        ["en"] = new { title = english.Title, synopsis = english.Synopsis },
    };

    private List<object> CreditsOf(IReadOnlyList<CreditSeed> credits)
        => credits.Select((credit, order) => (object)new { personId = _people[credit.Person], role = credit.Role, characterName = credit.Character, order }).ToList();

    private void Changed(string kind, string name, string? step = null)
    {
        Changes++;
        if (step is null)
        {
            SeederLog.Created(logger, kind, name);
        }
        else
        {
            SeederLog.Step(logger, kind, name, step);
        }
    }

    public (int Genres, int People, int Movies, int Series) Counts
        => (DemoContent.Genres.Count, DemoContent.People.Count, DemoContent.Movies.Count, DemoContent.Series.Count);
}
